#if UNITY_STANDALONE_WIN
using System;
using System.Diagnostics;       // Process
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using System.IO;                // Path.Combine
using Debug = UnityEngine.Debug; // 'Debug' 모호성 해결

public class SystemTrayController : MonoBehaviour
{
    // ===== Fields =====
    private static Thread     _msgThread;
    private static IntPtr     _msgHwnd;       // 트레이 콜백을 받을 메시지 전용 윈도우
    private static Win32.WndProc _wndProcKeep; // GC 방지용
    private static IntPtr     _hIcon;         // 트레이 아이콘 핸들
    private static Win32.NOTIFYICONDATA _nid;
    
    // ◀◀◀ [삭제] 핸들 관리는 TransparentWindow가 하므로 이 필드는 삭제합니다.
    // private static IntPtr     _mainUnityHwnd = IntPtr.Zero; 

    // 메인 스레드에서 App.Quit()을 호출하기 위한 플래그
    private static volatile int _quitRequested; 

    private static ushort     _atom; // ◀ _atom 선언

    // ===== Unity Lifecycle =====
    private void Awake()
    {
        Application.runInBackground = true; 
    }

    private void Start()
    {
        // ◀◀◀ [수정] 핸들 가져오는 코드 삭제
        // TransparentWindow.Awake()가 먼저 실행되어 핸들을 관리합니다.

        // 메시지 루프를 위한 별도 스레드 시작
        _msgThread = new Thread(MessageThread) 
        { 
            IsBackground = true, 
            Name = "SystemTrayMsgThread" 
        };
        _msgThread.Start();
        Debug.Log("[Tray] 메시지 스레드 시작");
    }

    private void Update()
    {
        // 백그라운드 스레드에서 요청한 종료를 메인 스레드에서 처리
        if (_quitRequested != 0)
        {
            _quitRequested = 0;
            Debug.Log("[Tray] '종료' 요청 수신. 애플리케이션을 종료합니다.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    private void OnApplicationQuit()
    {
        // 애플리케이션 종료 시 트레이 아이콘 및 메시지 윈도우 정리
        try
        {
            if (_msgHwnd != IntPtr.Zero)
            {
                if (_nid.cbSize != 0)
                {
                    Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _nid);
                }
                if (_hIcon != IntPtr.Zero) 
                { 
                    Win32.DestroyIcon(_hIcon); 
                    _hIcon = IntPtr.Zero; 
                }
                Win32.DestroyWindow(_msgHwnd);
                _msgHwnd = IntPtr.Zero;
            }
        }
        catch (Exception ex) 
        { 
            Debug.LogWarning($"[Tray] 정리 중 예외 발생: {ex.Message}");
        }
    }

    // ===== Main Window Handling =====

    /// <summary>
    /// [수정] Unity 메인 윈도우의 표시/숨김 상태를 토글합니다.
    /// </summary>
    private static void ToggleMainWindow()
    {
        // ◀◀◀ [수정] 창 제어 로직을 TransparentWindow에 위임
        if (TransparentWindow.Main != null)
        {
            // TransparentWindow에 있는 공용 메서드 호출
            TransparentWindow.ToggleVisibility();
        }
        else
        {
            Debug.LogError("[Tray] TransparentWindow.Main이 아직 준비되지 않았습니다!");
        }
    }

    /// <summary>
    /// 메인 스레드에 종료를 요청합니다. (백그라운드 스레드에서 호출)
    /// </summary>
    private static void RequestQuit()
    {
        _quitRequested = 1;
    }

    // ===== Message Thread & WndProc =====

    /// <summary>
    /// Win32 메시지 루프를 실행하는 스레드
    /// </summary>
    private static void MessageThread()
    {
        var hInst = Win32.GetModuleHandle(null);
        string className = "Unity_TrayMsgWindow_" + Guid.NewGuid().ToString("N");

        // 1) 윈도우 클래스 등록 (WNDCLASSEX)
        _wndProcKeep = TrayWndProc; // GC 방지
        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX)),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcKeep),
            hInstance = hInst,
            lpszClassName = className
        };
        _atom = Win32.RegisterClassEx(ref wc); // ◀ _atom 변수 사용
        if (_atom == 0)
        {
            Debug.LogError("[Tray] RegisterClassEx 실패");
            return;
        }

        // 2) 메시지 전용 윈도우 생성 (CreateWindowEx)
        _msgHwnd = Win32.CreateWindowEx(
            0, className, "Unity Tray Message Window", 0,
            0, 0, 0, 0,
            Win32.HWND_MESSAGE, IntPtr.Zero, hInst, IntPtr.Zero
        );
        if (_msgHwnd == IntPtr.Zero)
        {
            Debug.LogError("[Tray] CreateWindowEx 실패");
            Win32.UnregisterClass(className, hInst);
            return;
        }

        // 3) 트레이 아이콘 등록 (Shell_NotifyIcon)
        // (주의: 'myicon.ico' 파일은 Assets/StreamingAssets/ 폴더에 있어야 합니다)
        string iconPath = Path.Combine(Application.streamingAssetsPath, "myicon.ico");
        
        _hIcon = Win32.LoadImage(IntPtr.Zero, iconPath, Win32.IMAGE_ICON, 0, 0, Win32.LR_LOADFROMFILE);
        if (_hIcon == IntPtr.Zero)
        {
            Debug.LogWarning($"[Tray] 아이콘을 찾지 못했습니다: {iconPath}. 아이콘 없이 진행합니다.");
        }

        _nid = new Win32.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf(typeof(Win32.NOTIFYICONDATA)),
            hWnd   = _msgHwnd,
            uID    = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_TIP | ( _hIcon != IntPtr.Zero ? Win32.NIF_ICON : 0u ),
            uCallbackMessage = Win32.WM_TRAY,
            hIcon  = _hIcon,
            szTip  = Application.productName // 툴팁에 유니티 앱 이름 표시
        };
        
        if (!Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _nid))
        {
            Debug.LogError("[Tray] Shell_NotifyIcon(NIM_ADD) 실패");
            Win32.DestroyWindow(_msgHwnd);
            Win32.UnregisterClass(className, hInst);
            if (_hIcon != IntPtr.Zero) Win32.DestroyIcon(_hIcon);
            return;
        }

        // 4) 메시지 루프 실행
        Win32.MSG msg;
        while (Win32.GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }

        // 5) 스레드 종료 시 최종 정리 (WM_DESTROY 수신 후)
        try
        {
            if (_nid.cbSize != 0) Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _nid);
            if (_hIcon != IntPtr.Zero) { Win32.DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
            Win32.UnregisterClass(className, hInst);
            _msgHwnd = IntPtr.Zero;
        }
        catch { /* ignore */ }

        Debug.Log("[Tray] 메시지 스레드 종료");
    }

    /// <summary>
    /// 메시지 전용 윈도우의 콜백 함수 (WndProc)
    /// </summary>
    private static IntPtr TrayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_TRAY)
        {
            int lp = lParam.ToInt32();
            if (lp == Win32.WM_LBUTTONUP)
            {
                // [요청] 좌클릭: 창 토글
                ToggleMainWindow(); 
                return IntPtr.Zero;
            }
            else if (lp == Win32.WM_RBUTTONUP)
            {
                // [요청] 우클릭: 메뉴 표시
                ShowContextMenu(hWnd); 
                return IntPtr.Zero;
            }
        }
        else if (msg == Win32.WM_DESTROY)
        {
            Win32.PostQuitMessage(0); // 메시지 루프 종료
            return IntPtr.Zero;
        }
        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

/// <summary>
    /// [요청] 우클릭 시 '종료' 컨텍스트 메뉴를 표시합니다.
    /// </summary>
    private static void ShowContextMenu(IntPtr hWnd)
    {
        IntPtr hMenu = Win32.CreatePopupMenu();
        Win32.InsertMenu(hMenu, 0, 0x00000000, Win32.ID_EXIT, "종료");

        Win32.GetCursorPos(out Win32.POINT pt);
        Win32.SetForegroundWindow(hWnd); 

        //  [수정] hMenu 인수가 빠져있었습니다.
        int cmd = Win32.TrackPopupMenu(
            hMenu, // 1. 메뉴 핸들 (이것이 빠졌었습니다)
            Win32.TPM_RETURNCMD, // 2. 플래그
            pt.X,  // 3. X 위치
            pt.Y,  // 4. Y 위치
            0,     // 5. 예약됨 (0)
            hWnd,  // 6. 소유자 윈도우
            IntPtr.Zero // 7. rect (사용 안 함)
        );
        
        Win32.DestroyMenu(hMenu); // 메뉴 리소스 해제

        if (cmd == Win32.ID_EXIT)
        {
            // 1. '종료'를 클릭한 경우
            RequestQuit(); // 메인 스레드에 종료 요청
        }
        else if (cmd == 0)
        {
            // 2. 메뉴 바깥을 클릭하여 닫은 경우 (취소)
            //    현재 창이 "보이는" 상태였다면, "숨김" 상태로 변경합니다.
            if (!TransparentWindow.IsForceHidden)
            {
                ToggleMainWindow();
            }
        }
    }


    //################################################################################
    //##
    //##  Win32 API 정의 중첩 클래스 (한글 깨짐 수정 완료)
    //##
    //################################################################################
    private static class Win32
    {
        // ===== Delegates =====
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // ===== Structs =====
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; public uint lPrivate; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct NOTIFYICONDATA
        {
            public uint cbSize; public IntPtr hWnd; public uint uID; public uint uFlags; public uint uCallbackMessage; public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
        }

        // ===== Constants =====
        public const int WM_USER        = 0x0400;
        public const int WM_TRAY        = WM_USER + 1;
        public const int WM_DESTROY     = 0x0002;
        public const int WM_LBUTTONUP   = 0x0202;
        public const int WM_RBUTTONUP   = 0x0205;
        public const uint NIM_ADD       = 0x00000000;
        public const uint NIM_DELETE    = 0x00000002;
        public const uint NIF_MESSAGE   = 0x00000001;
        public const uint NIF_ICON      = 0x00000002;
        public const uint NIF_TIP       = 0x00000004;
        public const int SW_HIDE        = 0;
        public const int SW_SHOWNORMAL  = 1;
        public const uint TPM_RETURNCMD = 0x0100;
        public const int ID_EXIT        = 1001;
        public const uint IMAGE_ICON    = 1;
        public const uint LR_LOADFROMFILE = 0x00000010;
        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        // ===== P/Invoke (DLL Imports) =====
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
        
        [DllImport("user32.dll")]
	    public static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight,
            IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr hMenu);
        
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] // ◀ "종료" 한글용
        public static extern bool InsertMenu(IntPtr hMenu, int position, uint flags, int idNewItem, string lpNewItem);
        
        [DllImport("user32.dll")] 
        public static extern int TrackPopupMenu(IntPtr hmenu, uint flags, int x, int y, int r, IntPtr hwnd, IntPtr rect);

        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] // ◀ 아이콘 경로용
        public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
        
        [DllImport("user32.dll")] 
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] // ◀ 툴팁 한글용
        public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA data);
    }
}
#endif