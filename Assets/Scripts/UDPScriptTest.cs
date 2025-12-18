using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SimpleUdpListener : MonoBehaviour
{
    public int listenPort = 5005;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile string lastMessage;

    void Start()
    {
        IPAddress localIP = IPAddress.Parse("192.168.123.223");
        IPEndPoint localEP = new IPEndPoint(localIP, listenPort);

        udpClient = new UdpClient();
        udpClient.Client.Bind(localEP);

        receiveThread = new Thread(ReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();

        Debug.Log("UDP listener bound to " + localEP);
    }

    void ReceiveLoop()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

        while (true)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                lastMessage = Encoding.UTF8.GetString(data).Trim();
            }
            catch
            {
                break;
            }
        }
    }

    void Update()
    {
        if (lastMessage == "ON")
        {
            Debug.Log("Received ON from external sender");
            lastMessage = null;
        }
    }

    void OnApplicationQuit()
    {
        udpClient.Close();
        receiveThread.Abort();
    }
}
