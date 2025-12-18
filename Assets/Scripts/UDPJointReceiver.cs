using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;

public class G1JointControllerUDP : MonoBehaviour
{
    [Header("UDP")]
    public int listenPort = 5005; //port Unity listens on for incoming UDP packets

    [Header("Smoothing (optional)")]
    [Tooltip("0 = no smoothing, 1 = very smooth/laggy. might be useful for high hz updates if for some reason that crazy 500hz HighState is needed?")]
    [Range(0f, 1f)]
    public float smoothing = 0.0f; //simple low-pass style smoothing knob (adds visual lag)

    [System.Serializable]
    public class JointConfig
    {
        public GameObject jointObject; //the GameObject that represents this joint in the Unity rig
        public Vector3 rotationAxis = Vector3.forward; //local axis to rotate around (in jointObject local space)
        public float direction = 1f; //+1 or -1 to flip rotation direction if your rig is mirrored/inverted
    }

    //assign all 29 joints in Inspector
    //each one is a JointConfig (object + axis + direction)
    public JointConfig L_LEG_HIP_PITCH, L_LEG_HIP_ROLL, L_LEG_HIP_YAW, L_LEG_KNEE,
                       L_LEG_ANKLE_PITCH, L_LEG_ANKLE_ROLL,
                       R_LEG_HIP_PITCH, R_LEG_HIP_ROLL, R_LEG_HIP_YAW, R_LEG_KNEE,
                       R_LEG_ANKLE_PITCH, R_LEG_ANKLE_ROLL,
                       WAIST_YAW, WAIST_ROLL, WAIST_PITCH,
                       L_SHOULDER_PITCH, L_SHOULDER_ROLL, L_SHOULDER_YAW, L_ELBOW,
                       L_WRIST_ROLL, L_WRIST_PITCH, L_WRIST_YAW,
                       R_SHOULDER_PITCH, R_SHOULDER_ROLL, R_SHOULDER_YAW, R_ELBOW,
                       R_WRIST_ROLL, R_WRIST_PITCH, R_WRIST_YAW;

    Dictionary<string, JointConfig> jointMap; //name -> JointConfig lookup so we can apply by joint name

    //incoming packet format (must match python struct.pack("<II29f", ...))
    //layout (little-endian):
    // 0..3   : uint32 MAGIC ("JOIN")
    // 4..7   : uint32 seq (sequence number, helps detect drops/out-of-order)
    // 8..end : 29 floats (joint angles in radians, ordered by jointOrder)
    const uint MAGIC = 0x4A4F494Eu; //"JOIN" in ASCII bytes (little-endian interpretation)
    const int JOINT_COUNT = 29; //G1 has 29 joints being streamed
    const int PACKET_SIZE = 4 + 4 + (JOINT_COUNT * 4); //magic + seq + 29 float32s

    UdpClient _client; //socket wrapper for UDP receive
    Thread _thread; //background thread so Receive() doesn't block the Unity main thread
    volatile bool _running; //thread-safe-ish flag used to stop the receive loop

    //we cannot touch Unity objects (transforms) from a background thread
    //so we store the latest received values here, protected by a lock,
    //then apply them safely in Update() on the main thread.
    readonly object _lock = new object();
    float[] _latestQ = new float[JOINT_COUNT]; //most recent joint angles (radians)
    bool _hasNew = false; //set true when new data arrives; consumed in Update()

    //for optional debug / drop detection
    uint _lastSeq = 0; //last received sequence number
    bool _hasSeq = false; //tracks if we've ever received a packet

    //maps index -> joint name, MUST match sender ordering
    //this must exactly match the python sender's joint packing order
    readonly string[] jointOrder = new string[JOINT_COUNT]
    {
        "L_LEG_HIP_PITCH",
        "L_LEG_HIP_ROLL",
        "L_LEG_HIP_YAW",
        "L_LEG_KNEE",
        "L_LEG_ANKLE_PITCH",
        "L_LEG_ANKLE_ROLL",

        "R_LEG_HIP_PITCH",
        "R_LEG_HIP_ROLL",
        "R_LEG_HIP_YAW",
        "R_LEG_KNEE",
        "R_LEG_ANKLE_PITCH",
        "R_LEG_ANKLE_ROLL",

        "WAIST_YAW",
        "WAIST_ROLL",
        "WAIST_PITCH",

        "L_SHOULDER_PITCH",
        "L_SHOULDER_ROLL",
        "L_SHOULDER_YAW",
        "L_ELBOW",
        "L_WRIST_ROLL",
        "L_WRIST_PITCH",
        "L_WRIST_YAW",

        "R_SHOULDER_PITCH",
        "R_SHOULDER_ROLL",
        "R_SHOULDER_YAW",
        "R_ELBOW",
        "R_WRIST_ROLL",
        "R_WRIST_PITCH",
        "R_WRIST_YAW"
    };

    void Start()
    {
        //build a dictionary so we can do jointMap["WAIST_YAW"] -> JointConfig quickly
        //this avoids a giant if/else chain when applying rotations
        jointMap = new Dictionary<string, JointConfig>()
        {
            {"L_LEG_HIP_PITCH", L_LEG_HIP_PITCH},
            {"L_LEG_HIP_ROLL", L_LEG_HIP_ROLL},
            {"L_LEG_HIP_YAW", L_LEG_HIP_YAW},
            {"L_LEG_KNEE", L_LEG_KNEE},
            {"L_LEG_ANKLE_PITCH", L_LEG_ANKLE_PITCH},
            {"L_LEG_ANKLE_ROLL", L_LEG_ANKLE_ROLL},

            {"R_LEG_HIP_PITCH", R_LEG_HIP_PITCH},
            {"R_LEG_HIP_ROLL", R_LEG_HIP_ROLL},
            {"R_LEG_HIP_YAW", R_LEG_HIP_YAW},
            {"R_LEG_KNEE", R_LEG_KNEE},
            {"R_LEG_ANKLE_PITCH", R_LEG_ANKLE_PITCH},
            {"R_LEG_ANKLE_ROLL", R_LEG_ANKLE_ROLL},

            {"WAIST_YAW", WAIST_YAW},
            {"WAIST_ROLL", WAIST_ROLL},
            {"WAIST_PITCH", WAIST_PITCH},

            {"L_SHOULDER_PITCH", L_SHOULDER_PITCH},
            {"L_SHOULDER_ROLL", L_SHOULDER_ROLL},
            {"L_SHOULDER_YAW", L_SHOULDER_YAW},
            {"L_ELBOW", L_ELBOW},
            {"L_WRIST_ROLL", L_WRIST_ROLL},
            {"L_WRIST_PITCH", L_WRIST_PITCH},
            {"L_WRIST_YAW", L_WRIST_YAW},

            {"R_SHOULDER_PITCH", R_SHOULDER_PITCH},
            {"R_SHOULDER_ROLL", R_SHOULDER_ROLL},
            {"R_SHOULDER_YAW", R_SHOULDER_YAW},
            {"R_ELBOW", R_ELBOW},
            {"R_WRIST_ROLL", R_WRIST_ROLL},
            {"R_WRIST_PITCH", R_WRIST_PITCH},
            {"R_WRIST_YAW", R_WRIST_YAW}
        };

        //create a UDP socket bound to listenPort
        //this will receive datagrams sent to <this machine IP>:listenPort
        _client = new UdpClient(listenPort);
        _running = true;

        //start a background thread that blocks on _client.Receive()
        //Unity main thread stays responsive; we just copy data into _latestQ
        _thread = new Thread(ReceiveLoop);
        _thread.IsBackground = true; //dies when app exits (still best to stop it cleanly)
        _thread.Start();

        Debug.Log($"G1 UDP joint controller listening on port {listenPort}...");
    }

    void ReceiveLoop()
    {
        //this stores the sender's IP/port when we receive (not used beyond that)
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);

        while (_running)
        {
            try
            {
                //blocking call: waits until a UDP packet arrives
                byte[] data = _client.Receive(ref ep);

                //basic sanity checks: packet must be exactly expected size
                if (data == null || data.Length != PACKET_SIZE) continue;

                //check the 4-byte magic header so we ignore unrelated UDP traffic
                uint magic = BitConverter.ToUInt32(data, 0);
                if (magic != MAGIC) continue;

                //read sequence number (optional debugging/packet loss detection)
                uint seq = BitConverter.ToUInt32(data, 4);
                _lastSeq = seq;
                _hasSeq = true;

                //read 29 float32 joint angles (radians) starting at offset 8
                float[] q = new float[JOINT_COUNT];
                int baseOffset = 8;
                for (int i = 0; i < JOINT_COUNT; i++)
                {
                    q[i] = BitConverter.ToSingle(data, baseOffset + i * 4);
                }

                //copy into shared buffer; Update() will consume it on main thread
                lock (_lock)
                {
                    Array.Copy(q, _latestQ, JOINT_COUNT);
                    _hasNew = true;
                }
            }
            catch (SocketException)
            {
                //Receive() will throw when socket is closed; exit loop cleanly
                break;
            }
            catch (Exception)
            {
                //ignore transient parse/receive errors; keep listening
            }
        }
    }

    void Update()
    {
        float[] q = null;

        //grab latest joint angles snapshot (thread-safe), then release lock quickly
        lock (_lock)
        {
            if (_hasNew)
            {
                //clone so we can operate without holding the lock while rotating joints
                q = (float[])_latestQ.Clone();
                _hasNew = false;
            }
        }

        //no new packet since last Update() -> nothing to do
        if (q == null) return;

        //apply rotations for each joint index i using the jointOrder mapping
        for (int i = 0; i < JOINT_COUNT; i++)
        {
            //convert the packed index to a joint name
            string jointName = jointOrder[i];

            //safety: if the dictionary doesn't have this name, skip
            if (!jointMap.ContainsKey(jointName)) continue;

            //fetch per-joint config (target GameObject, axis, direction)
            JointConfig config = jointMap[jointName];
            if (config == null || config.jointObject == null) continue;

            //q[i] is in radians (robot side). Unity's AngleAxis expects degrees.
            float angleRad = q[i];
            float angleDeg = angleRad * Mathf.Rad2Deg * config.direction; //direction flips sign if needed

            //create a rotation around the configured axis by angleDeg
            //note: this sets localRotation directly, so your rig should be authored
            //such that "0" corresponds to the neutral pose you expect.
            Quaternion target = Quaternion.AngleAxis(angleDeg, config.rotationAxis.normalized);

            //optional smoothing: slerp from current rotation toward target
            //1 - smoothing is used as the interpolation fraction each frame:
            //  smoothing=0 -> t=1 -> snap
            //  smoothing close to 1 -> tiny t -> slow/laggy
            if (smoothing > 0f)
            {
                config.jointObject.transform.localRotation =
                    Quaternion.Slerp(config.jointObject.transform.localRotation, target, 1f - smoothing);
            }
            else
            {
                //no smoothing: directly set the joint's local rotation
                config.jointObject.transform.localRotation = target;
            }
        }

        //optional: quick debug heartbeat (watch seq increment to confirm packets arrive)
        //if (_hasSeq) Debug.Log($"seq {_lastSeq}");
    }

    void OnApplicationQuit()
    {
        //_running tells the thread loop to stop
        //Close() forces Receive() to throw SocketException so the loop exits immediately
        _running = false;
        try { _client?.Close(); } catch { }
        //wait briefly for the thread to exit (don't hang on quit)
        try { _thread?.Join(50); } catch { }
    }
}
