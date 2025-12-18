using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using System.Collections.Generic;

public class G1JointControllerUDP : MonoBehaviour
{
    [Header("UDP")]
    public int listenPort = 5005;

    [Header("Smoothing (optional)")]
    [Tooltip("0 = no smoothing, 1 = very smooth/laggy. might be useful for high hz updates if for some reason that crazy 500hz HighState is needed?")]
    [Range(0f, 1f)]
    public float smoothing = 0.0f;

    [System.Serializable]
    public class JointConfig
    {
        public GameObject jointObject;
        public Vector3 rotationAxis = Vector3.forward;
        public float direction = 1f; //+1 or -1
    }

    //assign all 29 joints in Inspector
    public JointConfig L_LEG_HIP_PITCH, L_LEG_HIP_ROLL, L_LEG_HIP_YAW, L_LEG_KNEE,
                       L_LEG_ANKLE_PITCH, L_LEG_ANKLE_ROLL,
                       R_LEG_HIP_PITCH, R_LEG_HIP_ROLL, R_LEG_HIP_YAW, R_LEG_KNEE,
                       R_LEG_ANKLE_PITCH, R_LEG_ANKLE_ROLL,
                       WAIST_YAW, WAIST_ROLL, WAIST_PITCH,
                       L_SHOULDER_PITCH, L_SHOULDER_ROLL, L_SHOULDER_YAW, L_ELBOW,
                       L_WRIST_ROLL, L_WRIST_PITCH, L_WRIST_YAW,
                       R_SHOULDER_PITCH, R_SHOULDER_ROLL, R_SHOULDER_YAW, R_ELBOW,
                       R_WRIST_ROLL, R_WRIST_PITCH, R_WRIST_YAW;

    Dictionary<string, JointConfig> jointMap;

    //incoming packet format (must match python struct.pack("<II29f", ...))
    const uint MAGIC = 0x4A4F494Eu; //"JOIN"
    const int JOINT_COUNT = 29;
    const int PACKET_SIZE = 4 + 4 + (JOINT_COUNT * 4);

    UdpClient _client;
    Thread _thread;
    volatile bool _running;

    readonly object _lock = new object();
    float[] _latestQ = new float[JOINT_COUNT];
    bool _hasNew = false;

    //for optional debug / drop detection
    uint _lastSeq = 0;
    bool _hasSeq = false;

    //maps index -> joint name, MUST match sender ordering
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
        //joint map
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

        //udp receive thread
        _client = new UdpClient(listenPort);
        _running = true;

        _thread = new Thread(ReceiveLoop);
        _thread.IsBackground = true;
        _thread.Start();

        Debug.Log($"G1 UDP joint controller listening on port {listenPort}...");
    }

    void ReceiveLoop()
    {
        IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);

        while (_running)
        {
            try
            {
                byte[] data = _client.Receive(ref ep);
                if (data == null || data.Length != PACKET_SIZE) continue;

                uint magic = BitConverter.ToUInt32(data, 0);
                if (magic != MAGIC) continue;

                uint seq = BitConverter.ToUInt32(data, 4);
                _lastSeq = seq;
                _hasSeq = true;

                float[] q = new float[JOINT_COUNT];
                int baseOffset = 8;
                for (int i = 0; i < JOINT_COUNT; i++)
                {
                    q[i] = BitConverter.ToSingle(data, baseOffset + i * 4);
                }

                lock (_lock)
                {
                    Array.Copy(q, _latestQ, JOINT_COUNT);
                    _hasNew = true;
                }
            }
            catch (SocketException)
            {
                break;
            }
            catch (Exception)
            {
                //ignore transient errors
            }
        }
    }

    void Update()
    {
        float[] q = null;

        lock (_lock)
        {
            if (_hasNew)
            {
                q = (float[])_latestQ.Clone();
                _hasNew = false;
            }
        }

        if (q == null) return;

        //apply rotations
        for (int i = 0; i < JOINT_COUNT; i++)
        {
            string jointName = jointOrder[i];
            if (!jointMap.ContainsKey(jointName)) continue;

            JointConfig config = jointMap[jointName];
            if (config == null || config.jointObject == null) continue;

            float angleRad = q[i];
            float angleDeg = angleRad * Mathf.Rad2Deg * config.direction;

            Quaternion target = Quaternion.AngleAxis(angleDeg, config.rotationAxis.normalized);

            if (smoothing > 0f)
            {
                config.jointObject.transform.localRotation =
                    Quaternion.Slerp(config.jointObject.transform.localRotation, target, 1f - smoothing);
            }
            else
            {
                config.jointObject.transform.localRotation = target;
            }
        }

        //optional: quick debug heartbeat
        //if (_hasSeq) Debug.Log($"seq {_lastSeq}");
    }

    void OnApplicationQuit()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        try { _thread?.Join(50); } catch { }
    }
}
