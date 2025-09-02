using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace ArcaneWeaver;

public static class Weaver
{
    static ModuleDefinition ModuleDefinition;

    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0].EndsWith(".dll"))
        {
            string targetPath = args[0];
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(targetPath));

            try
            {
                // Copy original DLL to a temp file
                File.Copy(targetPath, tempPath, true);

                // Open temp DLL for read/write
                using (var fs = new FileStream(tempPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.ReadWrite))
                {
                    ModuleDefinition = ModuleDefinition.ReadModule(fs);

                    foreach (var type in ModuleDefinition.Types)
                    {
                        // Packet hashing
                        if (type.BaseType?.FullName == "ArcaneNetworking.Packet")
                            InjectPacketHashRegister(type);

                        // RPC method weaving
                        if (type.BaseType?.FullName == "NetworkedComponent.Packet")
                        {
                            foreach (var method in type.Methods)
                            {
                                if (!method.CustomAttributes.Any(a => a.AttributeType.Name == "MethodRPCAttribute"))
                                    continue;

                                var invokeHandler = GenerateRPCUpackAndInvokeHandler(type, method);
                                int methodHash = InjectMethodRPCHashRegister(method, invokeHandler);
                                var packMethod = GeneratePackMethod(type, method, methodHash);
                                InjectRPCPreMethod(method, packMethod);
                            }
                        }
                    }

                    // Rewind stream and write changes
                    fs.Seek(0, SeekOrigin.Begin);
                    ModuleDefinition.Write(fs);
                }

                // Overwrite original DLL after weaving
                File.Copy(tempPath, targetPath, true);
                File.Delete(tempPath);

                Console.WriteLine("[Weaver] Successfully weaved: " + targetPath);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("[Weaver] Failed to weave: " + e);
                return 1;
            }
        }

        Console.WriteLine("[Weaver] Invalid arguments.");
        return 1;
    }

    static int StableHash(string hashString)
    {
        var hash = MD5.HashData(System.Text.Encoding.UTF8.GetBytes(hashString));
        return BitConverter.ToInt32(hash, 0);
    }

    static TypeReference GetTypeReferenceByFullName(string FullName)
    {
        // module = your target ModuleDefinition
        // Find the type inside the loaded assembly
        var registryTypeDef = ModuleDefinition.Types.FirstOrDefault(t => t.FullName == FullName);

        if (registryTypeDef == null)
            throw new Exception("Could not find Type: " + FullName + " in Project Acssembly!");

        // Import a reference to it
        return ModuleDefinition.ImportReference(registryTypeDef);
    }
    /// <summary>
    /// Injects a register method in NetworkStorage static constructor that registers packet IDs
    /// </summary>
    static int InjectPacketHashRegister(TypeDefinition type)
    {
        var networkStorageDef = GetTypeReferenceByFullName("ArcaneNetworking.NetworkStorage").Resolve();
        var packetDictionary = ModuleDefinition.ImportReference(networkStorageDef.Fields.First(f => f.Name == "PacketTypes"));
        int hash = StableHash(type.FullName);

        ////////////// WE ARE WEAVING THE STATIC CONSTRUCTOR NOW /////////////////

        var il = networkStorageDef.GetStaticConstructor().Body.GetILProcessor(); // Start Method Processing

        // Load dictionary
        il.Emit(OpCodes.Ldsfld, packetDictionary);

        // Push key (hash)
        il.Emit(OpCodes.Ldc_I4, hash);

        // Push Type
        il.Emit(OpCodes.Ldtoken, type);
        var getTypeFromHandle = ModuleDefinition.ImportReference(
            typeof(Type).GetMethod("GetTypeFromHandle", [typeof(RuntimeTypeHandle)])
        );
        il.Emit(OpCodes.Call, getTypeFromHandle);

        // Call Dictionary.Add(key, type)
        var dictAdd = ModuleDefinition.ImportReference(
        typeof(Dictionary<int, Type>).GetMethod("Add", [typeof(int), typeof(Type)]));

        il.Emit(OpCodes.Callvirt, dictAdd);

        return hash;
    }

    // <summary>
    /// Injects a register method in NetworkStorage static constructor that registers RPC method
    /// </summary>
    static int InjectMethodRPCHashRegister(MethodDefinition method, MethodDefinition invoker)
    {
        var registryDef = GetTypeReferenceByFullName("ArcaneNetworking.NetworkStorage").Resolve();
        var rpcDictField = registryDef.Fields.First(f => f.Name == "RPCMethods");
        int hash = StableHash(method.DeclaringType.FullName + "::" + method.Name);

        // In static ctor:
        var cctor = registryDef.GetStaticConstructor();
        var il = cctor.Body.GetILProcessor();

        // Load RPCMethods dictionary
        il.Emit(OpCodes.Ldsfld, rpcDictField);

        // Push hash
        il.Emit(OpCodes.Ldc_I4, hash);

        var networkedcomponentType = ModuleDefinition.Types.First(t => t.FullName == "ArcaneNetworking.NetworkedComponent");

        // Import Action<,>
        var actionTypeRef = ModuleDefinition.ImportReference(typeof(Action<,>));
        var genericAction = new GenericInstanceType(actionTypeRef);
        genericAction.GenericArguments.Add(ModuleDefinition.ImportReference(networkedcomponentType));
        genericAction.GenericArguments.Add(ModuleDefinition.ImportReference(typeof(uint)));

        // Import Dictionary<int, Action<NetworkedComponent>>
        var dictTypeRef = ModuleDefinition.ImportReference(typeof(Dictionary<,>));
        var genericDict = new GenericInstanceType(dictTypeRef);
        genericDict.GenericArguments.Add(ModuleDefinition.TypeSystem.Int32);
        genericDict.GenericArguments.Add(genericAction);

        // Get the Add method from the generic Dictionary type
        var addMethodDef = dictTypeRef.Resolve().Methods
            .First(m => m.Name == "Add" && m.Parameters.Count == 2);

        // Create the ctor reference for Action<NetworkedComponent, uint>(object, IntPtr)
        var actionCtor = genericAction.Resolve()
            .Methods.First(m => m.IsConstructor && m.Parameters.Count == 2 &&
                                m.Parameters[0].ParameterType.FullName == "System.Object" &&
                                m.Parameters[1].ParameterType.FullName == "System.IntPtr");


        il.Emit(OpCodes.Ldnull); // target (static method)
        il.Emit(OpCodes.Ldftn, invoker); // method pointer
        il.Emit(OpCodes.Newobj, actionCtor); // The constructor for creating a Action<NetworkedComponent>

        il.Emit(OpCodes.Callvirt, addMethodDef); // Add to dictionary

        return hash;
    }

    // <summary>
    /// Injects instructions behind an RPC method 
    /// </summary>
    static void InjectRPCPreMethod(MethodDefinition method, MethodDefinition packMethod)
    {
        bool isServerCommand = (bool)method.CustomAttributes
        .First(x => x.AttributeType.FullName == "ArcaneNetworking.MethodRPCAttribute")
        .ConstructorArguments[1].Value;

        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();

        for (int i = 0; i < method.Parameters.Count; i++)
            il.InsertBefore(first, il.Create(OpCodes.Ldarg, i + 1));

        il.InsertBefore(first, il.Create(OpCodes.Call, packMethod));

        if (isServerCommand) il.InsertBefore(first, il.Create(OpCodes.Ret));

    }

    // <summary>
    /// Generates a (packMethod) that pack the arguments that are sent into an RPC, as well as the packet type (RPC = 1),
    /// and the RPC data.
    /// </summary>
    static MethodDefinition GeneratePackMethod(TypeDefinition component, MethodDefinition rpc, int methodHash)
    {
        int packetHash = StableHash(component.DeclaringType.FullName + "::" + component.Name);

        // Get From MethodRPC Attribute args
        var rpcAttr = rpc.CustomAttributes.First(x => x.AttributeType.FullName == "ArcaneNetworking.MethodRPCAttribute");
        var channelAttrib = rpcAttr.ConstructorArguments[0].Value;
        var sendTimeAttrib = rpcAttr.ConstructorArguments[1].Value;
        var connsToSendAttrib = rpcAttr.ConstructorArguments[2].Value;

        // Convert connections argument
        var connsToSendTo = ((CustomAttributeArgument[])connsToSendAttrib)
            .Select(a => (uint)a.Value)
            .ToArray();

        // Import TypeReferences
        var rpcPacketType = GetTypeReferenceByFullName("ArcaneNetworking.RPCPacket");
        var netComponentType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkedComponent");
        var netNodeType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkedNode");
        var writerType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkWriter");
        var poolType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkPool");
        var messageLayerType = GetTypeReferenceByFullName("ArcaneNetworking.MessageLayer");

        // Get FieldReferences
        var netNodeField = ModuleDefinition.ImportReference(
            netComponentType.Resolve().Fields.First(f => f.Name == "NetworkedNode")
        );
        var netIDField = ModuleDefinition.ImportReference(
            netNodeField.FieldType.Resolve().Fields.First(f => f.Name == "NetID")
        );
        var messageLayerActiveField = ModuleDefinition.ImportReference(
            messageLayerType.Resolve().Fields.First(f => f.Name == "Active")
        );

        // Get MethodReferences
        var getComponentIndexMethod = ModuleDefinition.ImportReference(
            netComponentType.Resolve().Methods.First(m => m.Name == "GetIndex")
        );
        var getWriterBufferMethod = ModuleDefinition.ImportReference(
            writerType.Resolve().Methods.First(m => m.Name == "ToArraySegment")
        );
        var writeMethod = ModuleDefinition.ImportReference(
            writerType.Resolve().Methods.First(m => m.Name == "Write" && m.HasGenericParameters && m.Parameters.Count == 1)
        );

        // Import SendToConnections properly
        var sendMethodRef = ModuleDefinition.ImportReference(
            messageLayerType.Resolve().Methods.First(m =>
                m.Name == "SendToConnections" &&
                m.Parameters.Count == 3 &&
                m.Parameters[0].ParameterType.FullName == ModuleDefinition.ImportReference(typeof(ArraySegment<byte>)).FullName &&
                m.Parameters[1].ParameterType.FullName == ModuleDefinition.ImportReference(typeof(int)).FullName &&
                m.Parameters[2].ParameterType.FullName == ModuleDefinition.ImportReference(typeof(uint[])).FullName
            )
        );

        // Import static pool methods
        var poolGetWriterMethod = ModuleDefinition.ImportReference(
            poolType.Resolve().Methods.First(m => m.Name == "GetWriter" && m.IsStatic && m.Parameters.Count == 0)
        );
        var recycleWriterMethod = ModuleDefinition.ImportReference(
            poolType.Resolve().Methods.First(m => m.Name == "Recycle" && m.IsStatic && m.Parameters.Count == 1)
        );

        // Create pack method
        var packMethod = new MethodDefinition(
            "Pack_" + rpc.Name,
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
            ModuleDefinition.TypeSystem.Void
        );

        // Add same params as original RPC
        foreach (var p in rpc.Parameters)
            packMethod.Parameters.Add(new ParameterDefinition(p.Name, Mono.Cecil.ParameterAttributes.None, ModuleDefinition.ImportReference(p.ParameterType)));

        ////////////// WE ARE WEAVING THE PACK METHOD NOW /////////////////

        var il = packMethod.Body.GetILProcessor(); // Start Method Processing
        packMethod.Body.InitLocals = true;

        ///// WE ARE CREATING AN RPC PACKET OBJECT ///////

        var rpcPacketConstructor = ModuleDefinition.ImportReference(rpcPacketType.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters));

        // Store it into a local (so we can use it)
        // RPCPacket packet = 
        var rpcVar = new VariableDefinition(rpcPacketType);
        packMethod.Body.Variables.Add(rpcVar);

        il.Emit(OpCodes.Newobj, rpcPacketConstructor);
        il.Emit(OpCodes.Stloc, rpcVar);

        // Get fields and set values for RPCPacket definition
        var rpcCallerNetIDField = ModuleDefinition.ImportReference(rpcPacketType.Resolve().Fields.First(f => f.Name == "CallerNetID"));
        var rpcCallerCompIndexField = ModuleDefinition.ImportReference(rpcPacketType.Resolve().Fields.First(f => f.Name == "CallerCompIndex"));

        // NetID
        il.Emit(OpCodes.Ldloca, rpcVar); // Load the address of the local struct (RPCPacket) onto the stack

        il.Emit(OpCodes.Ldarg_0);  // Load "this" (NetworkedComponent)
        il.Emit(OpCodes.Ldfld, netNodeField);  // Load NetworkedNode from the abstract NetworkedComponent
        il.Emit(OpCodes.Ldfld, netIDField);  // Load NetID from NetworkedNode
        il.Emit(OpCodes.Stfld, rpcCallerNetIDField);  // Store the NetID into the RPCPacket struct field

        // CallerCompIndex 
        il.Emit(OpCodes.Ldloca, rpcVar); // Load the address of the local struct (RPCPacket) onto the stack

        il.Emit(OpCodes.Ldarg_0);                        // Load "this"
        il.Emit(OpCodes.Call, getComponentIndexMethod);  // Call GetIndex() on the component
        il.Emit(OpCodes.Stfld, rpcCallerCompIndexField); // Store result into CallerCompIndex

        /////// WE ARE NOW WRITING TO THE NETWORK WRITER BUFFER /////////

        // Load Writer object
        var writerVar = new VariableDefinition(writerType);
        packMethod.Body.Variables.Add(writerVar);

        // Call our GetWriter() method to retrieve the NetworkWriter from the pool
        il.Emit(OpCodes.Call, poolGetWriterMethod);
        il.Emit(OpCodes.Stloc, writerVar); // Allocate a writer variable

        // Write network message type to writer (RPC = 1)
        var writePacketTypeGeneric = new GenericInstanceMethod(writeMethod);
        writePacketTypeGeneric.GenericArguments.Add(ModuleDefinition.ImportReference(typeof(byte)));
        il.Emit(OpCodes.Ldloc, writerVar);
        il.Emit(OpCodes.Ldc_I4_S, (sbyte)1);
        il.Emit(OpCodes.Callvirt, writePacketTypeGeneric); // Push Packet byte

        // Write RPC Method ID hash to writer
        var writePacketHashGeneric = new GenericInstanceMethod(writeMethod);
        writePacketHashGeneric.GenericArguments.Add(ModuleDefinition.ImportReference(typeof(int)));
        il.Emit(OpCodes.Ldloc, writerVar);
        il.Emit(OpCodes.Ldc_I4, methodHash);
        il.Emit(OpCodes.Callvirt, writePacketHashGeneric); // Call generic

        // Write packet struct to writer
        var writePacketGeneric = new GenericInstanceMethod(writeMethod);
        writePacketGeneric.GenericArguments.Add(rpcPacketType.Resolve());
        il.Emit(OpCodes.Ldloc, writerVar);
        il.Emit(OpCodes.Ldloc, rpcVar); // Load packet (packet is struct)
        il.Emit(OpCodes.Callvirt, writePacketGeneric); // Call generic

        // Write each arg with writer.Write
        for (int i = 0; i < packMethod.Parameters.Count; i++)
        {
            var param = rpc.Parameters[i];

            var paramWriteGeneric = new GenericInstanceMethod(writeMethod);
            paramWriteGeneric.GenericArguments.Add(ModuleDefinition.ImportReference(packMethod.Parameters[i].ParameterType));
            il.Emit(OpCodes.Ldloc, writerVar);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Callvirt, paramWriteGeneric);
        }

        // Send over the network
        // Load MessageLayer.Active
        il.Emit(OpCodes.Ldsfld, messageLayerActiveField); // push Active onto stack

        // Load writer array
        il.Emit(OpCodes.Ldloc, writerVar);     // Push writer
        il.Emit(OpCodes.Call, getWriterBufferMethod); // Call writer.ToArraySegment()

        // Load channel
        int channelVal = (int)channelAttrib;
        il.Emit(OpCodes.Ldc_I4, channelVal);  // Push Channels enum

        // Create a local to store the array
        var arrayVar = new VariableDefinition(ModuleDefinition.ImportReference(typeof(uint[])));
        packMethod.Body.Variables.Add(arrayVar);

        // Push array size and create the array in IL
        il.Emit(OpCodes.Ldc_I4, connsToSendTo.Length);                    // push array length
        il.Emit(OpCodes.Newarr, ModuleDefinition.ImportReference(typeof(uint))); // create new uint[]
        il.Emit(OpCodes.Stloc, arrayVar);                                  // store in local

        // Fill the array
        for (int i = 0; i < connsToSendTo.Length; i++)
        {
            il.Emit(OpCodes.Ldloc, arrayVar);          // load array
            il.Emit(OpCodes.Ldc_I4, i);                // push index
            il.Emit(OpCodes.Ldc_I4, (int)connsToSendTo[i]); // push value
            il.Emit(OpCodes.Stelem_I4);                // array[index] = value
        }

        // Load the array when calling the method
        il.Emit(OpCodes.Ldloc, arrayVar);

        // Call the instance method
        il.Emit(OpCodes.Callvirt, sendMethodRef);  // Call Active.SendToConnections(...)

        // Recycle NetworkWriter
        il.Emit(OpCodes.Ldloc, writerVar);
        il.Emit(OpCodes.Call, recycleWriterMethod);

        il.Emit(OpCodes.Ret);

        component.Methods.Add(packMethod);

        return packMethod;
    }

    static MethodDefinition GenerateRPCUpackAndInvokeHandler(TypeDefinition component, MethodDefinition rpc)
    {
        var arraySegByteType = ModuleDefinition.ImportReference(typeof(ArraySegment<byte>));
        var ncType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkedComponent");
        var readerType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkReader");
        var poolType = GetTypeReferenceByFullName("ArcaneNetworking.NetworkPool"); ;
        var rpcPacketType = GetTypeReferenceByFullName("ArcaneNetworking.RPCPacket");

        // Pool methods
        var poolGetReader = ModuleDefinition.ImportReference(
            poolType.Resolve().Methods.First(m =>
                m.Name == "GetReader" &&
                m.IsStatic &&
                m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == arraySegByteType.FullName));

        var poolRecycleReader = ModuleDefinition.ImportReference(
            poolType.Resolve().Methods.First(m =>
                m.Name == "Recycle" &&
                m.IsStatic &&
                m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == readerType.FullName));

        // NetworkReader.Read<T>()
        var readGenericDef = ModuleDefinition.ImportReference(
            readerType.Resolve().Methods.First(m => m.Name == "Read" && m.HasGenericParameters && m.Parameters.Count == 0));

        // Method signature
        var unpack = new MethodDefinition(
            "Unpack_" + rpc.Name,
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static,
            ModuleDefinition.TypeSystem.Void);

        unpack.Parameters.Add(new ParameterDefinition("data", Mono.Cecil.ParameterAttributes.None, arraySegByteType));
        unpack.Parameters.Add(new ParameterDefinition("target", Mono.Cecil.ParameterAttributes.None, ncType));

        // Locals
        unpack.Body.InitLocals = true;

        var il = unpack.Body.GetILProcessor();

        var readerVar = new VariableDefinition(readerType);
        var packetVar = new VariableDefinition(rpcPacketType.Resolve()); // struct local
        unpack.Body.Variables.Add(readerVar);
        unpack.Body.Variables.Add(packetVar);

        // One local per RPC parameter
        var paramLocals = new List<VariableDefinition>(rpc.Parameters.Count);
        foreach (var p in rpc.Parameters)
        {
            // ensure type is imported into this module
            var pt = ModuleDefinition.ImportReference(p.ParameterType);
            var v = new VariableDefinition(pt);
            unpack.Body.Variables.Add(v);
            paramLocals.Add(v);
        }

        ////////////// WE ARE WEAVING THE UNPACK METHOD NOW ////////////////

        /// Packet Type and methodhash was read before IL Weave

        // reader = NetworkPool.GetReader(data)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, poolGetReader);
        il.Emit(OpCodes.Stloc, readerVar);

        // packet = reader.Read<RPCPacket>()
        {
            var readPacket = new GenericInstanceMethod(readGenericDef);
            readPacket.GenericArguments.Add(rpcPacketType);
            il.Emit(OpCodes.Ldloc, readerVar);
            il.Emit(OpCodes.Callvirt, readPacket);
            il.Emit(OpCodes.Stloc, packetVar);
        }

        // For each param: local_i = reader.Read<Ti>()
        for (int i = 0; i < rpc.Parameters.Count; i++)
        {
            var ti = ModuleDefinition.ImportReference(rpc.Parameters[i].ParameterType);
            var readTi = new GenericInstanceMethod(readGenericDef);
            readTi.GenericArguments.Add(ti);

            il.Emit(OpCodes.Ldloc, readerVar);
            il.Emit(OpCodes.Callvirt, readTi);
            il.Emit(OpCodes.Stloc, paramLocals[i]);
        }

        // Callvirt on the concrete component:
        // ((<DeclaringType>)target).Rpc(paramLocals...)
        var declaringCompType = ModuleDefinition.ImportReference(rpc.DeclaringType);
        var callTarget = ModuleDefinition.ImportReference(rpc); // instance method

        il.Emit(OpCodes.Ldarg_1); // target (NetworkedComponent)
        il.Emit(OpCodes.Castclass, declaringCompType); // cast to the concrete type

        for (int i = 0; i < paramLocals.Count; i++)
            il.Emit(OpCodes.Ldloc, paramLocals[i]);

        il.Emit(OpCodes.Callvirt, callTarget);

        // NetworkPool.Recycle(reader)
        il.Emit(OpCodes.Ldloc, readerVar);
        il.Emit(OpCodes.Call, poolRecycleReader);

        il.Emit(OpCodes.Ret);

        // Add method to the declaring type of the RPC (or a dispatcher type if you prefer)
        component.Methods.Add(unpack);

        return unpack;
    }


}