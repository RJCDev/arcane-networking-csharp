using System;

/// <summary>
/// If you wish to block the method from running locally, simply block the method with a return statement if we are not the server
/// </summary>

// TODO: Variable RPC
[AttributeUsage(AttributeTargets.Property)]
public class VarRPCAttribute : Attribute
{
    
}
    