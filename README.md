sproto-Unity
============

> A demo show how to use [sproto-Csharp](https://github.com/lvzixun/sproto-Csharp) in Unity.

How to send RPC
---------------

```csharp
SprotoType.Handshake.request req = new SprotoType.Handshake.request();
req.uid = uid;
req.token = token;

NetSender.Send<Protocol.Handshake>(req, (_) =>
{
	SprotoType.Handshake.response rsp = _ as SprotoType.Handshake.response;
	if (rsp.result == 0)
	{
	}
});
```

How to recv RPC
---------------

```csharp
SprotoTypeBase HeartbeatRsp(SprotoTypeBase _)
{
    SprotoType.Heartbeat.request req = _ as SprotoType.Heartbeat.request;
    return null; // can return a response
}

NetReceiver.AddHandler<Protocol.Heartbeat>(HeartbeatRsp);
```
