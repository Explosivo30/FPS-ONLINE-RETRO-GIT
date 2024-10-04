/*
 * - Edited by PrzemyslawNowaczyk (11.10.17)
 *   -----------------------------
 *   Deleting unused variables
 *   Changing obsolete methods
 *   Changing used input methods for consistency
 *   -----------------------------
 *
 * - Edited by NovaSurfer (31.01.17).
 *   -----------------------------
 *   Rewriting from JS to C#
 *   Deleting "Spawn" and "Explode" methods, deleting unused varibles
 *   -----------------------------
 * Just some side notes here.
 *
 * - Should keep in mind that idTech's cartisian plane is different to Unity's:
 *    Z axis in idTech is "up/down" but in Unity Z is the local equivalent to
 *    "forward/backward" and Y in Unity is considered "up/down".
 *
 * - Code's mostly ported on a 1 to 1 basis, so some naming convensions are a
 *   bit fucked up right now.
 *
 * - UPS is measured in Unity units, the idTech units DO NOT scale right now.
 *
 * - Default values are accurate and emulates Quake 3's feel with CPM(A) physics.
 *
 */

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DG.Tweening;
using Unity.Netcode;

// Contains the command the user wishes upon the character
struct Cmd
{
    public float forwardMove;
    public float rightMove;
    public float upMove;
}

public class CPMPlayer : NetworkBehaviour
{
    public Transform playerView; // Camera
    public Camera_Tilt cam;
    public float CameraFrequency;
    public float playerViewYOffset = 0.6f; // The height at which the camera is bound to
    public float xMouseSensitivity = 30.0f;
    public float yMouseSensitivity = 30.0f;
    //
    /*Frame occuring factors*/
    public float gravity = 20.0f;

    public float friction = 6; //Ground friction

    /* Movement stuff */
    public float moveSpeed = 7.0f;                // Ground move speed
    public float runAcceleration = 14.0f;         // Ground accel
    public float runDeacceleration = 10.0f;       // Deacceleration that occurs when running on the ground
    public float airAcceleration = 2.0f;          // Air accel
    public float airDecceleration = 2.0f;         // Deacceleration experienced when ooposite strafing
    public float airControl = 0.3f;               // How precise air control is
    public float sideStrafeAcceleration = 50.0f;  // How fast acceleration occurs to get up to sideStrafeSpeed when
    public float sideStrafeSpeed = 1.0f;          // What the max speed to generate when side strafing
    public float jumpSpeed = 8.0f;                // The speed at which the character's up axis gains when hitting jump
    public bool holdJumpToBhop = false;           // When enabled allows player to just hold jump button to keep on bhopping perfectly. Beware: smells like casual.
    
    /*print() style */
    public GUIStyle style;

    /*FPS Stuff */
    public float fpsDisplayRate = 4.0f; // 4 updates per sec

    private int frameCount = 0;
    private float dt = 0.0f;
    private float fps = 0.0f;

    CharacterController _controller;

    // Camera rotations
    private float rotX = 0.0f;
    private float rotY = 0.0f;
    float horizontalInput;
    float verticalInput;

    private Vector3 moveDirectionNorm = Vector3.zero;
    public Vector3 playerVelocity = Vector3.zero;
    private float playerTopVelocity = 0.0f;


    // Q3: players can queue the next jump just before he hits the ground
    private bool wishJump = false;

    // Used to display real time fricton values
    private float playerFriction = 0.0f;

    // Player commands, stores wish commands that the player asks for (Forward, back, jump, etc)
    private Cmd _cmd;

    //Unity Input System
    [SerializeField] InputSystemPlayer player;

    //STEP SYSTEM
    [Space]
    [Header("Step System")]
    [Space]
    [SerializeField] float walkStepTime = 1.2f;
    AudioSource stepSound;
    float walkStepTimeReset;
    [Space]
    [Header("Slide")]
    [Space]
    [SerializeField] Transform camTrans;
    [SerializeField] Transform handsTrans;

    [SerializeField] float slideSpeedDecay = 8f;
    [SerializeField] float cameraSlideDown = -1f;
    bool isSliding = false;
    float defaultYPos;
    float defaultHandsPos;
    float defaultMoveSpeed;
    [SerializeField] float headBobMagnitude = 0.04f;
    [SerializeField] float headBobSpeed = 1.2f;

    List<AudioClip> footstepSounds = new List<AudioClip>();
    AudioClip jumpAudios;
    AudioClip landAudios;
    bool hasLanded = false;

    FootstepSwapper swapper;
    /*
        [System.Serializable]
        public class AudioList
        {
            public List<StepAudios> stepAudioList;
            public List<LandAudios> landAudioList;
            public List<JumpAudios> jumpAudioList;
        }

        public AudioList listAudioList = new AudioList();
    */

    private void OnEnable()
    {
        /*
        

        player.jumpEvent += OnJump;
        player.slideEvent += OnSlide;
        */
        
    }


    private void OnDisable()
    {
        /*
        player.jumpEvent -= OnJump;
        player.slideEvent -= OnSlide;
        */
    }

    public override void OnNetworkSpawn()
    {
        if(!IsOwner) return;
        

        player.jumpEvent += OnJump;
        player.slideEvent += OnSlide;
        defaultYPos = camTrans.transform.localPosition.y;
        defaultHandsPos = handsTrans.transform.localPosition.y;
        defaultMoveSpeed = moveSpeed;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (playerView == null)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
                playerView = mainCamera.gameObject.transform;
        }

        // Put the camera inside the capsule collider
        playerView.position = new Vector3(
            transform.position.x,
            transform.position.y + playerViewYOffset,
            transform.position.z);

        //

        //SoundStep
        
        walkStepTimeReset = walkStepTime;
        


    }

    public override void OnNetworkDespawn()
    {
        player.jumpEvent -= OnJump;
        player.slideEvent -= OnSlide;
    }

    private void Awake()
    {
        //if (!IsOwner) return;
        
        
    }
    private void Start()
    {

        // Hide the cursor
        _controller = GetComponent<CharacterController>();
        stepSound = GetComponent<AudioSource>();
        swapper = GetComponent<FootstepSwapper>();
    }

    

    void Update()
    {
        if(!IsOwner) return;
        // Do FPS calculation
        frameCount++;
        dt += Time.deltaTime;
        if (dt > 1.0 / fpsDisplayRate)
        {
            fps = Mathf.Round(frameCount / dt);
            frameCount = 0;
            dt -= 1.0f / fpsDisplayRate;
                 }
        /* Ensure that the cursor is locked into the screen */
        if (Cursor.lockState != CursorLockMode.Locked) {
            if (Input.GetButtonDown("Fire1"))
                Cursor.lockState = CursorLockMode.Locked;
        }


        if (horizontalInput > 0)
        {
            cam.DoTilt(-1f);
        }
        else if (horizontalInput < 0)
        {
            cam.DoTilt(1f);

        }
        else
        {
            // Reset camera tilt if no horizontal input
            cam.DoTilt(0f);
        }


       

        /* Camera rotation stuff, mouse controls this shit */
        rotX -= player.lookValue.y * xMouseSensitivity * Time.deltaTime;
        rotY += player.lookValue.x * yMouseSensitivity * Time.deltaTime;

        // Clamp the X rotation
        if(rotX < -90)
            rotX = -90;
        else if(rotX > 90)
            rotX = 90;

        this.transform.rotation = Quaternion.Euler(0, rotY, 0); // Rotates the collider
        playerView.rotation     = Quaternion.Euler(rotX, rotY, 0); // Rotates the camera

        StepOnLanding();

        /* Movement, here's the important part */
        //QueueJump();

        Slide();
        HandleHeadbob();
            
        if (!isSliding && _controller.isGrounded)
            GroundMove();
        else if (!_controller.isGrounded)
            AirMove();

        // Move the controller
        _controller.Move(playerVelocity * Time.deltaTime);

        

        //STEP SOUND IF PLAYER MOVES
        DynamicStepSystem();

        

        /* Calculate top velocity */
        Vector3 udp = playerVelocity;
        udp.y = 0.0f;
        if(udp.magnitude > playerTopVelocity)
            playerTopVelocity = udp.magnitude;

        //Need to move the camera after the player has been moved because otherwise the camera will clip the player if going fast enough and will always be 1 frame behind.
        // Set the camera's position to the transform
        playerView.position = new Vector3(
            transform.position.x,
            transform.position.y + playerViewYOffset,
            transform.position.z);
    }


    /*******************************************************************************************************\
   |* MOVEMENT
   \*******************************************************************************************************/

    /**
     * Sets the movement direction based on player input
     */
    private void SetMovementDir()
    {
        _cmd.forwardMove = player.movementValue.y;
        _cmd.rightMove   = player.movementValue.x;

        horizontalInput = _cmd.rightMove;
    }

    /**
     * Queues the next jump just like in Q3
     */
    /*
    private void QueueJump()
    {
        if(holdJumpToBhop)
        {
            wishJump = Input.GetButtonDown("Jump");
            return;
        }

        if(Input.GetButtonDown("Jump") && !wishJump)
        {
            wishJump = true;
            
        }
            
        
    }
    */
   

    /**
     * Execs when the player is in the air
    */
    private void AirMove()
    {
        Vector3 wishdir;
        float wishvel = airAcceleration;
        float accel;
        
        SetMovementDir();

        wishdir =  new Vector3(_cmd.rightMove, 0, _cmd.forwardMove);
        wishdir = transform.TransformDirection(wishdir);

        float wishspeed = wishdir.magnitude;
        wishspeed *= moveSpeed;

        wishdir.Normalize();
        moveDirectionNorm = wishdir;

        // CPM: Aircontrol
        float wishspeed2 = wishspeed;
        if (Vector3.Dot(playerVelocity, wishdir) < 0)
            accel = airDecceleration;
        else
            accel = airAcceleration;
        // If the player is ONLY strafing left or right
        if(_cmd.forwardMove == 0 && _cmd.rightMove != 0)
        {
            if(wishspeed > sideStrafeSpeed)
                wishspeed = sideStrafeSpeed;
            accel = sideStrafeAcceleration;
        }

        Accelerate(wishdir, wishspeed, accel);
        if(airControl > 0)
            AirControl(wishdir, wishspeed2);
        // !CPM: Aircontrol

        // Apply gravity
        playerVelocity.y -= gravity * Time.deltaTime;
    }

    /**
     * Air control occurs when the player is in the air, it allows
     * players to move side to side much faster rather than being
     * 'sluggish' when it comes to cornering.
     */
    private void AirControl(Vector3 wishdir, float wishspeed)
    {
        float zspeed;
        float speed;
        float dot;
        float k;

        // Can't control movement if not moving forward or backward
        if(Mathf.Abs(_cmd.forwardMove) < 0.001 || Mathf.Abs(wishspeed) < 0.001)
            return;
        zspeed = playerVelocity.y;
        playerVelocity.y = 0;
        /* Next two lines are equivalent to idTech's VectorNormalize() */
        speed = playerVelocity.magnitude;
        playerVelocity.Normalize();

        dot = Vector3.Dot(playerVelocity, wishdir);
        k = 32;
        k *= airControl * dot * dot * Time.deltaTime;

        // Change direction while slowing down
        if (dot > 0)
        {
            playerVelocity.x = playerVelocity.x * speed + wishdir.x * k;
            playerVelocity.y = playerVelocity.y * speed + wishdir.y * k;
            playerVelocity.z = playerVelocity.z * speed + wishdir.z * k;

            playerVelocity.Normalize();
            moveDirectionNorm = playerVelocity;
        }

        playerVelocity.x *= speed;
        playerVelocity.y = zspeed; // Note this line
        playerVelocity.z *= speed;
    }

    /**
     * Called every frame when the engine detects that the player is on the ground
     */
    private void GroundMove()
    {
        Vector3 wishdir;

        // Do not apply friction if the player is queueing up the next jump
        if (!wishJump)
            ApplyFriction(1.0f);
        else
            ApplyFriction(0);

        SetMovementDir();

        wishdir = new Vector3(_cmd.rightMove, 0, _cmd.forwardMove);
        wishdir = transform.TransformDirection(wishdir);
        wishdir.Normalize();
        moveDirectionNorm = wishdir;

        var wishspeed = wishdir.magnitude;
        wishspeed *= moveSpeed;

        Accelerate(wishdir, wishspeed, runAcceleration);

        // Reset the gravity velocity
        playerVelocity.y = -gravity * Time.deltaTime;

        if(wishJump)
        {
            playerVelocity.y = jumpSpeed;
            PlayJumpSound();
            wishJump = false;
        }
    }

    /**
     * Applies friction to the player, called in both the air and on the ground
     */
    private void ApplyFriction(float t)
    {
        Vector3 vec = playerVelocity; // Equivalent to: VectorCopy();
        float speed;
        float newspeed;
        float control;
        float drop;

        vec.y = 0.0f;
        speed = vec.magnitude;
        drop = 0.0f;

        /* Only if the player is on the ground then apply friction */
        if(_controller.isGrounded)
        {
            control = speed < runDeacceleration ? runDeacceleration : speed;
            drop = control * friction * Time.deltaTime * t;
        }

        newspeed = speed - drop;
        playerFriction = newspeed;
        if(newspeed < 0)
            newspeed = 0;
        if(speed > 0)
            newspeed /= speed;

        playerVelocity.x *= newspeed;
        playerVelocity.z *= newspeed;
    }

    private void Accelerate(Vector3 wishdir, float wishspeed, float accel)
    {
        float addspeed;
        float accelspeed;
        float currentspeed;

        currentspeed = Vector3.Dot(playerVelocity, wishdir);
        addspeed = wishspeed - currentspeed;
        if(addspeed <= 0)
            return;
        accelspeed = accel * Time.deltaTime * wishspeed;
        if(accelspeed > addspeed)
            accelspeed = addspeed;

        playerVelocity.x += accelspeed * wishdir.x;
        playerVelocity.z += accelspeed * wishdir.z;
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(0, 0, 400, 100), "FPS: " + fps, style);
        var ups = _controller.velocity;
        ups.y = 0;
        GUI.Label(new Rect(0, 15, 400, 100), "Speed: " + Mathf.Round(ups.magnitude * 100) / 100 + "ups", style);
        GUI.Label(new Rect(0, 30, 400, 100), "Top Speed: " + Mathf.Round(playerTopVelocity * 100) / 100 + "ups", style);
    }


    private void DynamicStepSystem()
    {
        if(!isSliding) {
            hasLanded = false;
            if((_cmd.rightMove != 0f || _cmd.forwardMove != 0f) && _controller.isGrounded) //If is touching ground and is moving forward or to the sides
            {
            
                walkStepTime -= Time.deltaTime;
                if (walkStepTime <= 0f)
                {
                    swapper.CheckLayers();
                    int n = Random.Range(0, footstepSounds.Count);
                    
                    stepSound.clip = footstepSounds[n];

                    //stepSound.Play();
                    stepSound.PlayOneShot(stepSound.clip);
                        
                    ResetStepTime();
                }
            
            }
            else
            {
                ResetStepTime();
            }
        }
    }

    private void PlayJumpSound()
    {
        swapper.CheckLayers();
        stepSound.clip = jumpAudios;
        stepSound.PlayOneShot(stepSound.clip);
    }

    private void StepOnLanding()
    {
        
        if (!hasLanded &&_controller.isGrounded &&  playerVelocity.y < -7f) //Is grounded and wishJump is false so it just came in contact with the ground
        {
            hasLanded = true;
            swapper.CheckLayers();
            stepSound.clip = landAudios;
            stepSound.PlayOneShot(stepSound.clip); 
        }
    }

    private void ResetStepTime()
    {
        walkStepTime = walkStepTimeReset;
    }

    public void SwapFootSteps(FootstepCollection collection)
    {
        footstepSounds.Clear();
        for(int i = 0; i < collection.footstepSounds.Count; i++) 
        {
            footstepSounds.Add(collection.footstepSounds[i]);
        }

        jumpAudios = collection.jumpSound;
        landAudios = collection.landSound;
    }


    
    void Slide()
    {
        if (isSliding)
        {
            ResetStepTime();
            //No agachara la cabeza porque en HandleBOB con el movimiento siempre rota a traves de 
            camTrans.localPosition = Vector3.Lerp(new Vector3(camTrans.localPosition.x, camTrans.localPosition.y, camTrans.localPosition.z), new Vector3(camTrans.localPosition.x, cameraSlideDown, camTrans.localPosition.z), 0.11f);
            handsTrans.localPosition = Vector3.Lerp(new Vector3(handsTrans.localPosition.x, handsTrans.localPosition.y, handsTrans.localPosition.z), new Vector3(handsTrans.localPosition.x, cameraSlideDown, handsTrans.localPosition.z), 0.11f);


            //We slow the dash and stop it from going backwards
            
            if (moveSpeed < 0.1f)
            {
                moveSpeed = 0f;
            }
            else
            {
                //We move the center of the character controller for the dash
                _controller.center = new Vector3(0f, -0.28f, 0f);
                _controller.height = 1.28f;
                moveSpeed -= Time.deltaTime * slideSpeedDecay;
                playerVelocity.Normalize();
                float playerveloY = playerVelocity.y;
                playerVelocity *= moveSpeed;
                playerVelocity.y = playerveloY;
            }

        }
        else
        {
            isSliding = false;

            //Reset Transform to pre
            camTrans.localPosition = Vector3.Lerp(new Vector3(camTrans.localPosition.x, camTrans.localPosition.y, camTrans.localPosition.z), 
                new Vector3(camTrans.localPosition.x, defaultYPos, camTrans.localPosition.z), 0.11f);

            handsTrans.localPosition = Vector3.Lerp(new Vector3(handsTrans.localPosition.x, handsTrans.localPosition.y, handsTrans.localPosition.z), 
                new Vector3(handsTrans.localPosition.x, defaultHandsPos, handsTrans.localPosition.z), 0.11f);
            _controller.center = Vector3.zero;
            _controller.height = 2f;
            moveSpeed = defaultMoveSpeed;
        }
    }

    
    void HandleHeadbob()
    {
        if (!_controller.isGrounded) return;
        if (isSliding) return;
        
        if (Mathf.Abs(playerVelocity.x) > 0.1f || Mathf.Abs(playerVelocity.z) > 0.1)
        {
            camTrans.transform.localPosition = new Vector3(
                camTrans.transform.localPosition.x, camTrans.transform.localPosition.y + Mathf.Sin((moveSpeed * headBobSpeed) * Time.time) * headBobMagnitude , camTrans.transform.localPosition.z);

            //handsTrans.transform.localPosition = new Vector3(
                //handsTrans.transform.localPosition.x, handsTrans.transform.localPosition.y + Mathf.Sin((moveSpeed * headBobSpeed) * Time.time) * headBobMagnitude, handsTrans.transform.localPosition.z);

            //Debug.Log(Mathf.Sin((moveSpeed * headBobSpeed) * Time.time) * headBobMagnitude );
        }
        
    }

    private void OnSlide()
    {
        isSliding = !isSliding;
    }

    void OnJump()
    {
        wishJump = !wishJump; 
    }

    
}
