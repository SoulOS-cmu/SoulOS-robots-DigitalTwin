using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public class G1OdomControllerUDP : MonoBehaviour
{
    [Header("UDP")]
    public int listenPort = 5006;

    [Header("Target")]
    public Transform robotRoot; //the root transform of the whole robot rig

    [Header("Pose settings")]
    public bool applyFrameConversion = true; //true if robot odom is FLU/ENU and Unity is x-right y-up z-forward
    public Vector3 positionOffset = Vector3.zero; //optional offset for aligning starting pose
    public float positionScale = 1.0f; //1 unit = 1 meter (recommended)

    [Header("Smoothing (optional)")]
    [Range(0f, 1f)]
    public float smoothing = 0.0f; //0 snap, 1 laggy

    const uint MAGIC = 0x4F444F4Du; //"ODOM"
    const int PACKET_SIZE = 4 + 4 + (3 * 4) + (4 * 4); //magic + seq + pos3 + quat4

    UdpClient _client;
    Thread _thread;
    volatile bool _running;

    readonly object _lock = new object();
    bool _hasNew = false;

    //latest pose from UDP
    Vector3 _posRos; //as-sent from Ubuntu (robot frame)
    Quaternion _qRos; //Unity quaternion container, representing ROS-frame rotation

    void Start()
    {
        //bind socket and start receive thread
        _client = new UdpClient(listenPort);
        _running = true;

        _thread = new Thread(ReceiveLoop);
        _thread.IsBackground = true;
        _thread.Start();

        Debug.Log($"G1 UDP odom controller listening on port {listenPort}...");
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

                //uint seq = BitConverter.ToUInt32(data, 4); //available if you want drop detection

                int o = 8;

                float px = BitConverter.ToSingle(data, o + 0);
                float py = BitConverter.ToSingle(data, o + 4);
                float pz = BitConverter.ToSingle(data, o + 8);
                o += 12;

                //sent as w,x,y,z (robot/logger style)
                float qw = BitConverter.ToSingle(data, o + 0);
                float qx = BitConverter.ToSingle(data, o + 4);
                float qy = BitConverter.ToSingle(data, o + 8);
                float qz = BitConverter.ToSingle(data, o + 12);

                //unity Quaternion is (x,y,z,w)
                Quaternion qRos = new Quaternion(qx, qy, qz, qw);

                lock (_lock)
                {
                    _posRos = new Vector3(px, py, pz);
                    _qRos = qRos;
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
        if (robotRoot == null) return;

        Vector3 posRos;
        Quaternion qRos;
        bool has;

        lock (_lock)
        {
            has = _hasNew;
            if (!has) return;
            posRos = _posRos;
            qRos = _qRos;
            _hasNew = false;
        }

        //convert frames if needed
        Vector3 posUnity = applyFrameConversion ? MapRosToUnity(posRos) : posRos;
        Quaternion rotUnity = applyFrameConversion ? ConvertRosQuatToUnity(qRos) : qRos;

        //scale + offset
        posUnity = posUnity * positionScale + positionOffset;

        //smooth or snap
        if (smoothing > 0f)
        {
            robotRoot.localPosition = Vector3.Lerp(robotRoot.localPosition, posUnity, 1f - smoothing);
            robotRoot.localRotation = Quaternion.Slerp(robotRoot.localRotation, rotUnity, 1f - smoothing);
        }
        else
        {
            robotRoot.localPosition = posUnity;
            robotRoot.localRotation = rotUnity;
        }
    }

    //assumes robot odom uses x-forward, y-left, z-up (FLU), common in robotics
    //maps to Unity x-right, y-up, z-forward
    Vector3 MapRosToUnity(Vector3 v)
    {
        //ros: (x fwd, y left, z up)
        //unity: (x right, y up, z fwd)
        return new Vector3(-v.y, v.z, v.x);
    }

    Quaternion ConvertRosQuatToUnity(Quaternion qRos)
    {
        //rather than doing quaternion basis-change math directly,
        //rotate two basis vectors in "ros space", map those vectors into Unity, then rebuild rotation.

        //ros forward axis is +X, ros up axis is +Z (FLU assumption)
        Vector3 fRos = qRos * new Vector3(1f, 0f, 0f);
        Vector3 uRos = qRos * new Vector3(0f, 0f, 1f);

        Vector3 fU = MapRosToUnity(fRos);
        Vector3 uU = MapRosToUnity(uRos);

        //guard against degenerate cases
        if (fU.sqrMagnitude < 1e-8f || uU.sqrMagnitude < 1e-8f) return Quaternion.identity;

        return Quaternion.LookRotation(fU.normalized, uU.normalized);
    }

    void OnApplicationQuit()
    {
        _running = false;
        try { _client?.Close(); } catch { }
        try { _thread?.Join(50); } catch { }
    }
}
