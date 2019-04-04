﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

#if !UNITY_EDITOR
using Windows.Networking;
using Windows.Networking.Connectivity;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine.XR.WSA;
using UnityEngine.XR.WSA.WebCam;
#endif


public class NetworkSimulator : MonoBehaviour
{
    public static NetworkSimulator NetworkSimulatorSingleton = null;
    public int serverPort = 32123;
    public int targetPort = 32123;
    public string targetIP = "";
    public bool targetIPReady = false;
    public volatile bool connected = false;
    private ConcurrentQueue<messagePackage> outgoingQueue = null;
    public UnityEngine.UI.Text outputText = null;
    public string currentOutput = "";
    private byte[] incomingBuffer = null;
    private Stack<LineRenderer> lineRenderers = null;
    private ConcurrentQueue<lrStruct> incomingLineRenderers = null;
    private bool undoLineRenderer = false;
    public Material LineRendererDefaultMaterial = null;
#if !UNITY_EDITOR
    //public DatagramSocket udpClient = null;
    public StreamSocket tcpClient = null;
    public Windows.Storage.Streams.IOutputStream outputStream = null;
    public Windows.Storage.Streams.IInputStream inputStream = null;
    DataWriter writer = null;//new DataWriter(outputStream);
    DataReader reader = null;

    //udp broadcast listening
    DatagramSocket listenerSocket = null;
    const string udpPort = "32124";
#endif

    private struct lrStruct
    {
        public float r, g, b, a, pc, sw, ew;
        public Vector3[] verts;
    }

    private class messagePackage
    {
        public byte[] bytes = null;
        public messagePackage(byte[] b) { bytes = b; }
    }

    //public Mesh testMesh = null;
    // Start is called before the first frame update
    void Start()
    {
        if (NetworkSimulatorSingleton != null)
        {
            Destroy(this);
            return;
        }
        lineRenderers = new Stack<LineRenderer>();
        incomingLineRenderers = new ConcurrentQueue<lrStruct>();
        outgoingQueue = new ConcurrentQueue<messagePackage>();
        NetworkSimulatorSingleton = this;
        setupSocket();
#if !UNITY_EDITOR
        Listen();
#endif
    }
    public async void setupSocket()
    {

#if !UNITY_EDITOR
        //udpClient = new DatagramSocket();
        //udpClient.Control.DontFragment = true;
        tcpClient = new Windows.Networking.Sockets.StreamSocket();
        tcpClient.Control.NoDelay = false;
        tcpClient.Control.KeepAlive = false;
        tcpClient.Control.OutboundBufferSizeInBytes = 1500;
        while (!connected)
        {
            try
            {
                //await udpClient.BindServiceNameAsync("" + targetPort);
                await tcpClient.ConnectAsync(new HostName(targetIP), "" + targetPort);

                outputStream = tcpClient.OutputStream;
                inputStream = tcpClient.InputStream;
                writer = new DataWriter(outputStream);
                reader = new DataReader(inputStream);
                connected = true;
                incomingBuffer = new byte[0];
                while (connected)
                {
                    if(reader.UnconsumedBufferLength>4)
                    {
                        int incomingSize = reader.ReadInt32();
                        if(incomingSize>0&&incomingSize < 100000)
                        {
                            await reader.LoadAsync((uint)incomingSize);//preloads the buffer with the data which makes the following not needed.
                            /*
                            while (reader.UnconsumedBufferLength<incomingSize)
                            {
                                System.Threading.Tasks.Task.Delay(100).Wait();
                            }
                            */

                            int packetType = reader.ReadInt32();
                            float r = reader.ReadSingle();
                            float g = reader.ReadSingle();
                            float b = reader.ReadSingle();
                            float a = reader.ReadSingle();
                            int count = reader.ReadInt32();// this is actually just for padding...
                            float sw = reader.ReadSingle();
                            float ew = reader.ReadSingle();
                            byte[] packet = new byte[incomingSize-32];
                            reader.ReadBytes(packet);
                            if(packetType==4&&packet.Length>0)
                            {
                                lrStruct l = new lrStruct();
                                l.r = r;
                                l.g = g;
                                l.b = b;
                                l.a = a;
                                l.pc = count;
                                l.sw = sw;
                                l.ew = ew;
                                l.verts = new Vector3[count];
                                for(int i = 0; i < count; i++)//Dan actually wrote this one from scratch, so might be bugged.
                                {                 
                                    l.verts[i]=new Vector3(BitConverter.ToSingle(packet, i*12+0), BitConverter.ToSingle(packet, i * 12 + 4),
                                        BitConverter.ToSingle(packet, i * 12 + 8));
                                }
                                incomingLineRenderers.Enqueue(l);
                            }
                            if (packetType == 5)
                                undoLineRenderer = true;


                        }
                        else
                        {
                            //TODO Handle it.
                        }

                    }
                }


                //outputStream = await udpClient.GetOutputStreamAsync(new HostName(targetIP), "" + targetPort);
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
                connected = false;
                return;
            }
        }
#endif
    }
#if !UNITY_EDITOR

    private async void Listen()
    {
        listenerSocket = new DatagramSocket();
        listenerSocket.MessageReceived += udpMessageReceived;
        await listenerSocket.BindServiceNameAsync(udpPort);
    }

    async void udpMessageReceived(DatagramSocket socket, DatagramSocketMessageReceivedEventArgs args)
    {
        if (!targetIPReady)
        {
            DataReader reader = args.GetDataReader();
            uint len = reader.UnconsumedBufferLength;
            string msg = reader.ReadString(len);

            string remoteHost = args.RemoteAddress.DisplayName;
            reader.Dispose();
            textOut("" + msg);
            await Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                targetIP = msg;
                targetIPReady = true;
                textOut("UDP Set up. "+targetIP+" "+targetIPReady);
            });
        }
    }
#endif



    public void SendTestData()
    {
#if !UNITY_EDITOR
        if (!connected)
        {
            textOut("Not connected");
            return;
        }
        try
        {
            Vector3 location = new Vector3();
            Quaternion rotation = new Quaternion();
            byte[] bytes = new byte[4 + 12 + 20]; // 4 bytes per float
            System.Buffer.BlockCopy(BitConverter.GetBytes(36 + (256 * 4)), 0, bytes, 0, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(2), 0, bytes, 4, 4);//type of packet
            System.Buffer.BlockCopy(BitConverter.GetBytes(location.x), 0, bytes, 8, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(location.y), 0, bytes, 12, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(location.z), 0, bytes, 16, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(rotation.x), 0, bytes, 20, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(rotation.y), 0, bytes, 24, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(rotation.z), 0, bytes, 28, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(rotation.w), 0, bytes, 32, 4);

            byte[] testBytes = new byte[256 * 4];

            for(int i = 0; i < 256; i++)
            {
                testBytes[i] = (byte)i;
            }
            bytes = Combine(bytes, testBytes);

            if (bytes.Length > 0)
            {
                enqueueOutgoing(bytes);
                textOut("Outgoing data enqueued.");
            }
        }
        catch (Exception e)
        {
            textOut(""+ e.ToString());
            Debug.Log(e.ToString());
            return;
        }
#endif
    }



    public void textOut(string o)
    {
        if (outputText == null)
            return;

        outputText.text = currentOutput += "\n" + o;
        if(outputText.text.Length>1000)
        {
            outputText.text = currentOutput.Substring(currentOutput.Length - 500);
        }
        
    }
#if !UNITY_EDITOR
    void FixedUpdate()
    {
        if (!outgoingQueue.IsEmpty)
        {
            messagePackage mp = null;
            outgoingQueue.TryDequeue(out mp);
            if (mp != null)
            {
                sendOutgoingPacket(mp);
                textOut("Packet Sent.");
            }
        }

        if(!incomingLineRenderers.IsEmpty)
        {
           
            lrStruct l = new lrStruct();
            if(incomingLineRenderers.TryDequeue(out l))
            {
                LineRenderer lr = this.gameObject.AddComponent<LineRenderer>();
                lr.material = new Material(LineRendererDefaultMaterial);//copy
                lr.material.color = new Color(l.r, l.g, l.b, l.a);
                lr.startWidth = l.sw;
                lr.endWidth = l.ew;
                lr.endColor = lr.startColor = new Color(l.r, l.g, l.b, l.a);
                /* some helpful notes
                LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();
                lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                lineRenderer.widthMultiplier = 0.2f;
                lineRenderer.positionCount = lengthOfLineRenderer;

                // A simple 2 color gradient with a fixed alpha of 1.0f.
                float alpha = 1.0f;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(c1, 0.0f), new GradientColorKey(c2, 1.0f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(alpha, 0.0f), new GradientAlphaKey(alpha, 1.0f) }
                );
                lineRenderer.colorGradient = gradient;
                */
            }
        }
        //SendHeadsetLocation();
    }

    private async void flush()
    {
        await writer.StoreAsync();
        //await writer.FlushAsync();
    }

    private async void sendOutgoingPacket(messagePackage sendData)
    {
        try
        {
            if (sendData.bytes.Length > 1000000)
            {
                Debug.Log("Packet of length " + sendData.bytes.Length + " waiting to go out... But can't.. Because it is probably too huge...");
                return;
            }
            lock (outputStream)
            {

                writer.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
                writer.ByteOrder = Windows.Storage.Streams.ByteOrder.LittleEndian;
                writer.WriteBytes(sendData.bytes);
                flush();
                Debug.Log("Sent " + sendData.bytes.Length + " bytes.");
            }


        }
        catch (Exception e)
        {
           textOut("" + e.ToString());
           Debug.Log(e.ToString());
           return;
        }
    }


    private void enqueueOutgoing(byte[] bytes)
    {
        outgoingQueue.Enqueue(new messagePackage(bytes));
    }

#endif

    

    //stolen useful code.
    public static byte[] Combine(byte[] first, byte[] second)
    {
        byte[] ret = new byte[first.Length + second.Length];
        System.Buffer.BlockCopy(first, 0, ret, 0, first.Length);
        System.Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
        return ret;
    }
    public static byte[] Combine(byte[] first, byte[] second, byte[] third)
    {
        byte[] ret = new byte[first.Length + second.Length + third.Length];
        System.Buffer.BlockCopy(first, 0, ret, 0, first.Length);
        System.Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
        System.Buffer.BlockCopy(third, 0, ret, first.Length + second.Length,
                         third.Length);
        return ret;
    }
    
}
