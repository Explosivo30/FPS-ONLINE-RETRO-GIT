using Unity.Netcode;
using UnityEngine;
using Cinemachine;

namespace Knife.RealBlood.SimpleController
{
    public class Weapon : NetworkBehaviour
    {
        
        public CinemachineVirtualCamera virtualCamera;
        public LayerMask ShotMask;
        public float Damage = 10f;

        public float DefaultFov = 60f;
        public float AimFov = 35f;
        public bool AutomaticFire;
        public float AutomaticFireRate = 10;

        [SerializeField] protected Animator handsAnimator;

        bool isAiming = false;

        [SerializeField] InputSystemPlayer player;
        bool shooting = false;

        float currentFov;

        float lastFireTime;
        float fireInterval
        {
            get
            {
                return 1f / AutomaticFireRate;
            }
        }

        public float CurrentFov
        {
            get
            {
                return currentFov;
            }
        }

        public override void OnNetworkSpawn()
        {
            player.shootEvent += OnShooting;
            lastFireTime = -fireInterval;
            
        }

        public override void OnNetworkDespawn()
        {
            currentFov = DefaultFov;
            player.shootEvent -= OnShooting;
            
        }

        private void Awake()
        {
        }

        private void OnEnable()
        {
        }

        void Start()
        {
            if (!IsOwner) return;
        }

        private void OnDisable()
        {
        }

        private void OnShooting()
        {
            
            shooting = true;
        }

        void Update()
        {
            if (!IsOwner) return;
            
            if (AutomaticFire)
            {
                
                if (shooting && Time.time > lastFireTime + fireInterval)
                {
                   
                    lastFireTime = Time.time;
                    shooting = false;
                    RequestShotServerRpc();
                }
            }
            else
            {
                
                if (shooting && Time.time > lastFireTime + fireInterval)
                {
                    
                    lastFireTime = Time.time;
                    shooting = false;
                    RequestShotServerRpc();
                }
            }

            if (Input.GetMouseButtonDown(1))
            {
                isAiming = true;
            }
            if (Input.GetMouseButtonUp(1))
            {
                isAiming = false;
            }

            currentFov = Mathf.Lerp(currentFov, isAiming ? AimFov : DefaultFov, Time.deltaTime * 12f);
        }

        [ServerRpc]
        private void RequestShotServerRpc(ServerRpcParams rpcParams = default)
        {
            
            Shot();
            ShotClientRpc();
        }

        [ClientRpc]
        private void ShotClientRpc(ClientRpcParams rpcParams = default)
        {
            Shot();
        }

        protected virtual void EndFire()
        {

        }

        protected virtual void Shot()
        {
            
            handsAnimator.Play("Shot", 0, 0);
            

            Ray r = new Ray(virtualCamera.transform.position, virtualCamera.transform.forward);
            RaycastHit hitInfo;

            if (Physics.Raycast(r, out hitInfo, 1000, ShotMask, QueryTriggerInteraction.Ignore))
            {
                var hittable = hitInfo.collider.GetComponent<IHittable>();
                if (hittable != null)
                {
                    DamageData[] damage = new DamageData[1]
                    {
                        new DamageData()
                        {
                            amount = Damage,
                            direction = r.direction,
                            normal = hitInfo.normal,
                            point = hitInfo.point
                        }
                    };

                    hittable.TakeDamage(damage);
                }
            }

            DebugShot(r, hitInfo);
        }

        protected void DebugShot(Ray r, RaycastHit hitInfo)
        {
            if (hitInfo.collider != null)
            {
                Debug.DrawLine(r.origin, hitInfo.point, Color.green, 3f);
            }
            else
            {
                Debug.DrawLine(r.origin, r.GetPoint(1000), Color.red, 3f);
            }
        }

        public Vector3 GetLookDirection()
        {
            return virtualCamera.transform.forward;
        }
    }
}
