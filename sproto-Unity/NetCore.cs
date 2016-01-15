using System;
using System.IO;
using System.Threading;
using System.Net.Sockets;
using System.Collections.Generic;
using Sproto;
using SprotoType;
using UnityEngine;

public delegate void SocketConnected();

public class NetCore
{
    private static Socket socket;

    public static bool logined;
    public static bool enabled;

    private static int CONNECT_TIMEOUT = 3000;
    private static ManualResetEvent TimeoutObject;

    private static Queue<byte[]> recvQueue = new Queue<byte[]>();

    private static SprotoPack sendPack = new SprotoPack();
    private static SprotoPack recvPack = new SprotoPack();

    private static SprotoStream sendStream = new SprotoStream();
    private static SprotoStream recvStream = new SprotoStream();

    private static ProtocolFunctionDictionary protocol = Protocol.Instance.Protocol;
    private static Dictionary<long, ProtocolFunctionDictionary.typeFunc> sessionDict;

    private static AsyncCallback connectCallback = new AsyncCallback(Connected);
    private static AsyncCallback receiveCallback = new AsyncCallback(Receive);

    public static void Init()
    {
        byte[] receiveBuffer = new byte[1 << 16];
        recvStream.Write(receiveBuffer, 0, receiveBuffer.Length);
        recvStream.Seek(0, SeekOrigin.Begin);

        sessionDict = new Dictionary<long, ProtocolFunctionDictionary.typeFunc>();
    }

    public static void Connect(string host, int port, SocketConnected socketConnected)
    {
        Disconnect();

        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.BeginConnect(host, port, connectCallback, socket);

        TimeoutObject = new ManualResetEvent(false);
        TimeoutObject.Reset();

        if (TimeoutObject.WaitOne(CONNECT_TIMEOUT, false))
        {
            Receive();
            socketConnected();
        }
        else
        {
            Debug.Log("Connect Timeout");
        }
    }

    private static void Connected(IAsyncResult ar)
    {
        socket.EndConnect(ar);
        TimeoutObject.Set();
    }

    public static void Disconnect()
    {
        if (connected)
        {
            socket.Close();
        }
    }

    public static bool connected
    {
        get
        {
            return socket != null && socket.Connected;
        }
    }

    public static void Send<T>(SprotoTypeBase rpc = null, long? session = null)
    {
        Send(rpc, session, protocol[typeof(T)]);
    }

    private static int MAX_PACK_LEN = (1 << 16) - 1;
    private static void Send(SprotoTypeBase rpc, long? session, int tag)
    {
        if (!connected || !enabled)
        {
            return;
        }

        package pkg = new package();
        pkg.type = tag;

        if (session != null)
        {
            pkg.session = (long)session;
            sessionDict.Add((long)session, protocol[tag].Response.Value);
        }

        sendStream.Seek(0, SeekOrigin.Begin);
        int len = pkg.encode(sendStream);
        if (rpc != null)
        {
            len += rpc.encode(sendStream);
        }

        byte[] data = sendPack.pack(sendStream.Buffer, len);
        if (data.Length > MAX_PACK_LEN)
        {
            Debug.Log("data.Length > " + MAX_PACK_LEN + " => " + data.Length);
            return;
        }

        sendStream.Seek(0, SeekOrigin.Begin);
        sendStream.WriteByte((byte)(data.Length >> 8));
        sendStream.WriteByte((byte)data.Length);
        sendStream.Write(data, 0, data.Length);

        try {
            socket.Send(sendStream.Buffer, sendStream.Position, SocketFlags.None);
        }
        catch (Exception e) {
            Debug.LogWarning(e.ToString());
        }
    }

    private static int receivePosition;
    public static void Receive(IAsyncResult ar = null)
    {
        if (!connected)
        {
            return;
        }

        if (ar != null)
        {
            try {
                receivePosition += socket.EndReceive(ar);
            }
            catch (Exception e) {
                Debug.LogWarning(e.ToString());
            }
        }

        int i = recvStream.Position;
        while (receivePosition >= i + 2)
        {
            int length = (recvStream[i] << 8) | recvStream[i+1];

            int sz = length + 2;
            if (receivePosition < i + sz)
            {
                break;
            }

            recvStream.Seek(2, SeekOrigin.Current);

            if (length > 0)
            {
                byte[] data = new byte[length];
                recvStream.Read(data, 0, length);
                recvQueue.Enqueue(data);
            }

            i += sz;
        }

        if (receivePosition == recvStream.Buffer.Length)
        {
            recvStream.Seek(0, SeekOrigin.End);
            recvStream.MoveUp(i, i);
            receivePosition = recvStream.Position;
            recvStream.Seek(0, SeekOrigin.Begin);
        }

        try {
            socket.BeginReceive(recvStream.Buffer, receivePosition,
                recvStream.Buffer.Length - receivePosition,
                SocketFlags.None, receiveCallback, socket);
        }
        catch (Exception e) {
            Debug.LogWarning(e.ToString());
        }
    }

    public static void Dispatch()
    {
        package pkg = new package();

        if (recvQueue.Count > 20)
        {
            Debug.Log("recvQueue.Count: " + recvQueue.Count);
        }

        while (recvQueue.Count > 0)
        {
            byte[] data = recvPack.unpack(recvQueue.Dequeue());
            int offset = pkg.init(data);

            int tag = (int)pkg.type;
            long session = (long)pkg.session;

            if (pkg.HasType)
            {
                RpcReqHandler rpcReqHandler = NetReceiver.GetHandler(tag);
                if (rpcReqHandler != null)
                {
                    SprotoTypeBase rpcRsp = rpcReqHandler(protocol.GenRequest(tag, data, offset));
                    if (pkg.HasSession)
                    {
                        Send(rpcRsp, session, tag);
                    }
                }
            }
            else
            {
                RpcRspHandler rpcRspHandler = NetSender.GetHandler(session);
                if (rpcRspHandler != null)
                {
                    ProtocolFunctionDictionary.typeFunc GenResponse;
                    sessionDict.TryGetValue(session, out GenResponse);
                    rpcRspHandler(GenResponse(data, offset));
                }
            }
        }
    }

}
