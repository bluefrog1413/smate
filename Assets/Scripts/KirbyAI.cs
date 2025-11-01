using System.Collections;
using UnityEngine;

public class KirbyAI : MonoBehaviour
{
    // 설정값 (Inspector 창에서 수정 가능)
    public float moveSpeed = 1.5f;     // 걷는 속도
    public float minIdleTime = 1.0f;   // 최소한 가만히 있는 시간
    public float maxIdleTime = 3.0f;   // 최대한 가만히 있는 시간
    public float minMoveTime = 2.0f;   // 최소한 걷는 시간
    public float maxMoveTime = 4.0f;   // 최대한 걷는 시간
    public float changeYDirectionChance = 0.5f;

    // --- 새로 추가된 설정값 ---
    [Range(0, 1)] // 0% ~ 100% 사이로 조절
    public float sighChance = 0.3f;    // 한숨 쉴 확률 (0.3 = 30%)
    public float sighDuration = 2.0f;  // ★★★ 한숨 애니메이션의 '실제 길이' (초 단위)
    // ------------------------

    private Animator anim;
    private SpriteRenderer spriteRenderer;

    // --- 화면 경계를 위한 변수들 (이전과 동일) ---
    private Camera mainCamera;
    private float minX, maxX, minY, maxY;
    private float spriteHalfWidth, spriteHalfHeight;

    void Start()
    {
        anim = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // --- 화면 경계 계산 (이전과 동일) ---
        mainCamera = Camera.main;
        spriteHalfWidth = spriteRenderer.bounds.size.x / 2f;
        spriteHalfHeight = spriteRenderer.bounds.size.y / 2f;
        Vector3 minScreenPos = mainCamera.ViewportToWorldPoint(new Vector3(0, 0, 0));
        Vector3 maxScreenPos = mainCamera.ViewportToWorldPoint(new Vector3(1, 1, 0));
        minX = minScreenPos.x + spriteHalfWidth;
        maxX = maxScreenPos.x - spriteHalfWidth;
        minY = minScreenPos.y + spriteHalfHeight;
        maxY = maxScreenPos.y - spriteHalfHeight;
        // ------------------------------------------

        StartCoroutine(ThinkAndAct());
    }

    // --- 화면 경계 제한 (이전과 동일) ---
    void LateUpdate()
    {
        Vector3 currentPosition = transform.position;
        currentPosition.x = Mathf.Clamp(currentPosition.x, minX, maxX);
        currentPosition.y = Mathf.Clamp(currentPosition.y, minY, maxY);
        transform.position = currentPosition;
    }


    // --- AI 행동 로직 (수정됨) ---
    IEnumerator ThinkAndAct()
    {
        while (true)
        {
            // --- 1. IDLE 또는 SIGH 상태 ---
            // '가만히 있기'로 기본 설정
            anim.SetBool("isWalking", false);

            // 랜덤 확률로 '한숨'을 쉴지 '그냥 가만히 있을지' 결정
            if (Random.value < sighChance)
            {
                // --- 1a. 한숨 쉬기 ---
                // "doSigh" 방아쇠를 당김 (애니메이터가 Sigh 상태로 전환)
                anim.SetTrigger("doSigh");

                // ★ 한숨 애니메이션이 '끝날 때까지' 기다려줌
                // (Sigh Duration을 애니메이션 길이와 맞춰야 함)
                yield return new WaitForSeconds(sighDuration);
            }
            else
            {
                // --- 1b. 그냥 가만히 있기 ---
                // 1초~3초 사이의 랜덤한 시간 동안 기다림
                float idleTime = Random.Range(minIdleTime, maxIdleTime);
                yield return new WaitForSeconds(idleTime);
            }

            // (이제 한숨 또는 아이들 상태가 끝났음)

            // --- 2. 걷기 상태 (이전과 동일) ---
            anim.SetBool("isWalking", true);

            float xDirection = (Random.Range(0, 2) == 0) ? -1f : 1f;
            float yDirection = 0f;
            if (Random.value < changeYDirectionChance)
            {
                yDirection = (Random.Range(0, 2) == 0) ? -1f : 1f;
            }

            spriteRenderer.flipX = (xDirection == -1f);

            float moveTime = Random.Range(minMoveTime, maxMoveTime);
            float timer = 0;

            while (timer < moveTime)
            {
                Vector3 moveVector = new Vector3(xDirection, yDirection, 0);
                transform.Translate(moveVector.normalized * moveSpeed * Time.deltaTime);

                timer += Time.deltaTime;
                yield return null;
            }
        }
    }
}