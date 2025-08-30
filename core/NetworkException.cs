using System;

namespace ArcaneNetworking;

/// TODO

// Exception for System packets not containing the right info
[Serializable]
public class SystemPacketPayloadException : Exception
{
    // Constructors
    public SystemPacketPayloadException() 
        : base() 
    { }
    public SystemPacketPayloadException(string message)
        : base(message)
    { }
}

// Exception for making sure that the RPC method body doesn't run on the client
[Serializable]
public class RPCNotServerException : Exception
{
    // Constructors
    public RPCNotServerException() 
        : base() 
    { }
    public RPCNotServerException(string message)
        : base(message)
    { }
}

// Exception for when trying to call an RPC when you are not the owner of an object
[Serializable]
public class NotNetworkOwnerException : Exception
{
    // Constructors
    public NotNetworkOwnerException() 
        : base() 
    { }
    public NotNetworkOwnerException(string message)
        : base(message)
    { }
}

