using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using System.Threading;

[RequireComponent(typeof(Camera))]
public class TransparentWindow : MonoBehaviour
{
    public static TransparentWindow Main = null;
    public static Camera Camera = null; //Used instead of Camera.main

    [Tooltip("What GameObject layers should trigger window focus when the mouse passes over objects?")] //
    [SerializeField] LayerMask clickLayerMask = ~0;

    [Tooltip("Allows Input to be detected even when focus is lost")] //
    [SerializeField] bool useSystemInput = false;

    [Tooltip("Should the window be fullscreen?")] //
    [SerializeField] bool fullscreen = true;

    [Tooltip("Force the window to match ScreenResolution")] //
    [SerializeField] bool customResolution = true;

    [Tooltip("Resolution the overlay should run at")] //
    [SerializeField] Vector2Int screenResolution = new Vector2Int(1280, 720);

    [Tooltip("The framerate the overlay should try to run at")] //
    [SerializeField] int targetFrameRate = 30;
    
    private static long _isForceHidden = 0;

    // 이 부분(IsForceHidden 속성)은 수정할 필요 없습니다.
    // _isForceHidden이 long이 되면서 Interlocked.Read가 정상 작동합니다.
    public static bool IsForceHidden
    {
        get { return Interlocked.Read(ref _isForceHidden) == 1; }
    }

    /////////////////////
    //Windows DLL stuff//
    /////////////////////

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetLayeredWindowAttributes")]
    static extern int SetLayeredWindowAttributes(IntPtr hwnd, int crKey, byte bAlpha, int dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
    static extern bool GetWindowRect(IntPtr hwnd, out Rectangle rect);

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImportAttribute("user32.dll")]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    static extern int SetWindowPos(IntPtr hwnd, int hwndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("Dwmapi.dll")]
    static extern uint DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Rectangle margins);

    // ◀◀◀ [추가 2] SystemTrayController와의 연동을 위해 필요한 API
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);


    const int GWL_STYLE = -16;
    const uint WS_POPUP = 0x80000000;
    const uint WS_VISIBLE = 0x10000000;
    const int HWND_TOPMOST = -1;

    const int WM_SYSCOMMAND = 0x112;
    const int WM_MOUSE_MOVE = 0xF012;

    // ◀◀◀ [추가 3] ShowWindowAsync를 위한 상수
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;

    int fWidth;
    int fHeight;
    IntPtr hwnd = IntPtr.Zero;
    Rectangle margins;
    Rectangle windowRect;

    void Awake()
    {
        Main = this;

        Camera = GetComponent<Camera>();
        Camera.backgroundColor = new Color();
        Camera.clearFlags = CameraClearFlags.SolidColor;

        if (fullscreen && !customResolution)
        {
            screenResolution = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
        }

        Screen.SetResolution(screenResolution.x, screenResolution.y, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);

        Application.targetFrameRate = targetFrameRate;
        Application.runInBackground = true;

#if !UNITY_EDITOR
        fWidth = screenResolution.x;
        fHeight = screenResolution.y;
        margins = new Rectangle() { Left = -1 };
        hwnd = GetActiveWindow(); // ◀ hwnd가 여기서 설정됩니다.

        if (!GetWindowRect(hwnd, out windowRect)) // ◀ ! (not) 연산자 추가 (GetWindowRect는 성공 시 true 반환)
        {
            // 성공 시 0이 아닌 값을 반환하므로, ! (not) 을 붙여 실패했을 때(0) 로그를 남기도록 수정
            Debug.Log("GetWindowRect 성공 (초기 위치 설정)");
        }

        SetWindowLong(hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
        SetWindowPos(hwnd, HWND_TOPMOST, windowRect.Left, windowRect.Top, fWidth, fHeight, 32 | 64);
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
#endif
    }

    void Update()
    {
        if (useSystemInput)
        {
            SystemInput.Process();
        }

        // [3. 수정] 스레드 안전하게 값을 읽어옵니다.
        if (Interlocked.Read(ref _isForceHidden) == 1)
        {
            return;
        }

        SetClickThrough();
    }

    // ◀◀◀ [4. 교체] ToggleVisibility 로직 전체를 스레드 안전하게 변경
    public static void ToggleVisibility()
    {
        if (Main == null || Main.hwnd == IntPtr.Zero)
        {
            Debug.LogWarning("[Window] ToggleVisibility: TransparentWindow가 아직 준비되지 않았습니다.");
            return;
        }

        // ◀◀◀ [수정] 'int'를 'long'으로 변경
        long currentState = Interlocked.Read(ref _isForceHidden);

        // ◀◀◀ [수정] 'int'를 'long'으로 변경
        long newState = 1 - currentState;

        Interlocked.Exchange(ref _isForceHidden, newState);

        if (newState == 1) // 1 = 숨김
        {
            ShowWindowAsync(Main.hwnd, SW_HIDE);
            Debug.Log("[Window] 강제 숨김 (비활성화)");
        }
        else // 0 = 표시
        {
            ShowWindowAsync(Main.hwnd, SW_SHOWNORMAL);
            BringWindowToTop(Main.hwnd);
            SetForegroundWindow(Main.hwnd);
            Debug.Log("[Window] 강제 표시 (활성화)");
        }
    }
    //Returns true if the cursor is over a UI element or 2D physics object
    bool FocusForInput()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem && eventSystem.IsPointerOverGameObject())
        {
            return true;
        }

        Vector2 pos = Camera.ScreenToWorldPoint(Input.mousePosition);
        return Physics2D.OverlapPoint(pos, clickLayerMask);
    }

    void SetClickThrough()
    {
        var focusWindow = FocusForInput();

        //Get window position
        GetWindowRect(hwnd, out windowRect);

#if !UNITY_EDITOR
        if (focusWindow)
        {
            SetWindowLong(hwnd, -20, ~(((uint)524288) | ((uint)32)));
            SetWindowPos(hwnd, HWND_TOPMOST, windowRect.Left, windowRect.Top, fWidth, fHeight, 32 | 64);
        }
        else
        {
            SetWindowLong(hwnd, GWL_STYLE, WS_POPUP | WS_VISIBLE);
            SetWindowLong(hwnd, -20, (uint)524288 | (uint)32);
            SetLayeredWindowAttributes(hwnd, 0, 255, 2);
            SetWindowPos(hwnd, HWND_TOPMOST, windowRect.Left, windowRect.Top, fWidth, fHeight, 32 | 64);
        }
#endif
    }

    public static void DragWindow()
    {
#if !UNITY_EDITOR
        if (Screen.fullScreenMode != FullScreenMode.Windowed)
        {
            return;
        }
        ReleaseCapture();
        SendMessage(Main.hwnd, WM_SYSCOMMAND, WM_MOUSE_MOVE, 0);
        Input.ResetInputAxes();
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}