using UnityEngine;

namespace Rooftop
{
    [RequireComponent(typeof(PracticeCat), typeof(CharacterController))]
    public sealed class CatVisual : MonoBehaviour
    {
        [SerializeField] Transform visual;
        [SerializeField] Animator animator;
        [SerializeField, Min(0)] float jumpOffset = .2f;
        [SerializeField, Min(.01f)] float offsetSpeed = 2;

        static readonly int Speed = Animator.StringToHash("Speed");
        PracticeCat cat;
        CharacterController controller;
        Vector3 restPosition, previousPosition;
        float offset;

        void Awake()
        {
            cat = GetComponent<PracticeCat>();
            controller = GetComponent<CharacterController>();
            if (visual) restPosition = visual.localPosition;
        }

        void OnEnable() => previousPosition = transform.position;

        void LateUpdate()
        {
            Vector3 displacement = transform.position - previousPosition;
            previousPosition = transform.position;
            if (!visual || !cat.IsSpawned) return;

            bool simulatedLocally = cat.IsServer || cat.IsOwner;
            Vector3 velocity = controller.enabled && simulatedLocally
                ? controller.velocity
                : displacement / Mathf.Max(Time.deltaTime, .0001f);
            velocity.y = 0;
            if (animator) animator.SetFloat(Speed, velocity.magnitude, .1f, Time.deltaTime);

            // Remote proxies have a disabled controller and no replicated grounded field.
            bool onGround = simulatedLocally ? cat.IsGrounded() : Physics.Raycast(
                transform.TransformPoint(controller.center), Vector3.down,
                controller.height * .5f + controller.skinWidth + .05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            offset = Mathf.MoveTowards(offset, onGround ? 0 : jumpOffset, offsetSpeed * Time.deltaTime);
            visual.localPosition = restPosition + Vector3.up * offset;
        }

        void OnDisable()
        {
            offset = 0;
            if (visual) visual.localPosition = restPosition;
        }
    }
}
