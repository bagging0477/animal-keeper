using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private float smoothTime = 0.08f;
    [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
    [Tooltip("목표 위치와 이 거리(유닛) 이상 떨어져 있으면 따라가지 않고 즉시 붙는다. 씬 시작 시 맵 생성기가 플레이어를 " +
        "스폰 지점으로 옮기거나 순간이동했을 때, 카메라가 원점에서 맵을 가로질러 휙 날아가는 화면(멀미 유발)을 막는다.")]
    [SerializeField, Min(0f)] private float snapDistance = 4f;

    private Vector3 velocity;
    private bool hasSnapped;

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        if (!hasSnapped || (desiredPosition - transform.position).sqrMagnitude >= snapDistance * snapDistance)
        {
            hasSnapped = true;
            transform.position = desiredPosition;
            velocity = Vector3.zero;
            return;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref velocity, smoothTime);
    }
}
