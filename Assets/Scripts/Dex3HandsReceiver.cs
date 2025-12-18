using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class Dex3HandUdpController : MonoBehaviour
{
    [Header("UDP")]
    public int listenPort = 5010;

    [Header("Smoothing (optional)")]
    [Range(0f, 1f)]
    public float smoothing = 0.0f; //0=no smoothing, 0.2=nice

    [Serializable]
    public class JointConfig
    {
        public GameObject jointObject;
        public Vector3 rotationAxis = Vector3.forward;
        public float direction = 1f;
    }

    [Header("Left hand joints (7)")]
    public JointConfig[] left = new JointConfig[7];

    [Header("Right hand joints (7)")]
    public JointConfig[] right = new JointConfig[7];

    const uint MAGIC = 0x33584544u; //"DEX3"
    const int DOF = 7;
    const int FLOATS = 14;
    const int PACKET_SIZE = 4 + 4 + (FLOATS * 4);

    UdpClient _client;
    Thread _thread;
    volatile bool _running;

    readonly object _lock = new object();
    float[] _latest = new float[FLOATS];
    bool _hasNew = false;

    uint _lastSeq = 0;
    bool _hasSeq = false;

    void Start()
    {
        //validates arrays
        if (left == null || left.Length != DOF) left = new JointConfig[DOF];
        if (right == null || right.Length != DOF) right = new JointConfig[DOF];

        //starts udp listener
        _client = new UdpClient(listenPort);
        _running = true;

        _thread = new Thread(ReceiveLoop);
        _thread.IsBackground = true;
        _thread.Start();

        Debug.Log($"Dex3HandUdpController listening on port {listenPort}...");
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

                float[] tmp = new float[FLOATS];
                int baseOffset = 8;

                for (int i = 0; i < FLOATS; i++)
                {
                    tmp[i] = BitConverter.ToSingle(data, baseOffset + i * 4);
                }

                lock (_lock)
                {
                    Array.Copy(tmp, _latest, FLOATS);
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
        float[] v = null;

        lock (_lock)
        {
            if (_hasNew)
            {
                v = (float[])_latest.Clone();
                _hasNew = false;
            }
        }

        if (v == null) return;

        //apply left 0..6
        for (int i = 0; i < DOF; i++)
        {
            ApplyJoint(left[i], v[i]);
        }

        //apply right 7..13
        for (int i = 0; i < DOF; i++)
        {
            ApplyJoint(right[i], v[DOF + i]);
        }

        //optional debug
        //if (_hasSeq) Debug.Log($"dex3 seq {_lastSeq}");
    }

    void ApplyJoint(JointConfig cfg, float angleRad)
    {
        if (cfg == null || cfg.jointObject == null) return;

        float angleDeg = angleRad * Mathf.Rad2Deg * cfg.direction;
        Quaternion target = Quaternion.AngleAxis(angleDeg, cfg.rotationAxis.normalized);

        if (smoothing > 0f)
        {
            cfg.jointObject.transform.localRotation =
                Quaternion.Slerp(cfg.jointObject.transform.localRotation, target, 1f - smoothing);
        }
        else
        {
            cfg.jointObject.transform.localRotation = target;
        }
    }

    void OnApplicationQuit()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        try { _thread?.Join(50); } catch { }
    }
}
