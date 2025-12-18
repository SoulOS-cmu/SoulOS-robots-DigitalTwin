using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

public class UnityAudioToRobotUDP_RingBuffer : MonoBehaviour
{
    [Header("Ubuntu target")]
    public string ubuntuIp = "192.168.123.164";
    public int ubuntuPort = 6000;

    [Header("Audio")]
    public int targetSampleRate = 16000;
    [Range(0f, 2f)]
    public float gain = 1.0f;

    const uint MAGIC = 0x30445541u; //"AUD0"

    //20ms frames @ 16kHz
    const int FRAME_SAMPLES = 320;
    const int RING_CAPACITY = FRAME_SAMPLES * 16; //320ms jitter buffer

    //lock-free ring buffer (audio thread -> main thread)
    short[] ring = new short[RING_CAPACITY];
    volatile int writePos = 0;
    volatile int readPos = 0;

    //resampler state
    int unitySampleRate;
    double step; //source samples per output sample (srcHz/dstHz)
    double srcPos = 0.0; //position in current callback buffer (can be slightly negative)
    float lastMono = 0.0f; //last mono sample from previous callback buffer

    //network (main thread only)
    UdpClient udp;
    IPEndPoint endpoint;
    uint seq = 0;
    bool ready = false;

    void Start()
    {
        udp = new UdpClient();
        endpoint = new IPEndPoint(IPAddress.Parse(ubuntuIp), ubuntuPort);

        unitySampleRate = AudioSettings.outputSampleRate;
        step = (double)unitySampleRate / (double)targetSampleRate;

        ready = true;
        Debug.Log($"Audio ring sender: {unitySampleRate}Hz -> {targetSampleRate}Hz step={step:F4}");
    }

    //==================================================
    // AUDIO THREAD
    //==================================================
    void OnAudioFilterRead(float[] data, int channels)
    {
        int srcSamples = data.Length / channels;
        if (srcSamples <= 0) return;

        int producedGuard = 0;
        int producedLimit = 4096; //prevents any runaway loop if something is weird

        while (producedGuard++ < producedLimit)
        {
            //ring full -> drop audio (never block audio thread)
            int nextWrite = (writePos + 1) % RING_CAPACITY;
            if (nextWrite == readPos)
                break;

            float s0, s1;
            double frac;

            //case 1: srcPos is negative -> interpolate between lastMono and current buffer's first sample
            if (srcPos < 0.0)
            {
                //srcPos in (-1, 0): -1 means exactly lastMono, 0 means exactly first sample
                frac = srcPos + 1.0;

                s0 = lastMono;
                s1 = ReadMono(data, channels, 0);
            }
            else
            {
                int i0 = (int)srcPos;
                int i1 = i0 + 1;

                //need i1 valid for interpolation
                if (i1 >= srcSamples)
                    break;

                frac = srcPos - i0;

                s0 = ReadMono(data, channels, i0);
                s1 = ReadMono(data, channels, i1);
            }

            float mono = (float)((1.0 - frac) * s0 + frac * s1);
            mono *= gain;

            ring[writePos] = FloatToInt16(mono);
            writePos = nextWrite;

            srcPos += step;
        }

        //save last sample for boundary interpolation into next callback
        lastMono = ReadMono(data, channels, srcSamples - 1);

        //advance srcPos into the next callback's coordinate frame
        //if we consumed beyond this buffer, srcPos will be >= srcSamples; subtracting keeps fractional remainder
        srcPos -= srcSamples;

        //clamp: keep srcPos in a sane range so it doesn't drift
        //allow slightly negative (for boundary interpolation), but not huge
        if (srcPos < -1.0) srcPos = -1.0;
        if (srcPos > (double)srcSamples) srcPos = 0.0;
    }

    float ReadMono(float[] data, int channels, int sample)
    {
        int baseIdx = sample * channels;
        float sum = 0f;
        for (int c = 0; c < channels; c++)
            sum += data[baseIdx + c];
        return sum / channels;
    }

    short FloatToInt16(float x)
    {
        x = Mathf.Clamp(x, -1f, 1f);
        return (short)(x * 32767f);
    }

    //==================================================
    // MAIN THREAD
    //==================================================
    void Update()
    {
        if (!ready || udp == null || endpoint == null)
            return;

        while (AvailableSamples() >= FRAME_SAMPLES)
            SendFrame();
    }

    int AvailableSamples()
    {
        int w = writePos;
        int r = readPos;
        return (w >= r) ? (w - r) : (RING_CAPACITY - r + w);
    }

    void SendFrame()
    {
        byte[] pkt = new byte[12 + FRAME_SAMPLES * 2];

        WriteU32(pkt, 0, MAGIC);
        WriteU32(pkt, 4, seq++);
        WriteU16(pkt, 8, (ushort)FRAME_SAMPLES);
        WriteU16(pkt, 10, 0);

        int o = 12;
        for (int i = 0; i < FRAME_SAMPLES; i++)
        {
            short s = ring[readPos];
            readPos = (readPos + 1) % RING_CAPACITY;

            pkt[o++] = (byte)(s & 0xFF);
            pkt[o++] = (byte)((s >> 8) & 0xFF);
        }

        udp.Send(pkt, pkt.Length, endpoint);
    }

    void WriteU32(byte[] b, int o, uint v)
    {
        b[o+0] = (byte)(v & 0xFF);
        b[o+1] = (byte)((v >> 8) & 0xFF);
        b[o+2] = (byte)((v >> 16) & 0xFF);
        b[o+3] = (byte)((v >> 24) & 0xFF);
    }

    void WriteU16(byte[] b, int o, ushort v)
    {
        b[o+0] = (byte)(v & 0xFF);
        b[o+1] = (byte)((v >> 8) & 0xFF);
    }

    void OnDestroy()
    {
        ready = false;
        try { udp?.Close(); } catch {}
        udp = null;
    }
}
