using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace ArcaneNetworking;


public static class ExtensionMethods
{
    public static Guid GenerateGUID(this string hashData)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(hashData));
            Guid result = new Guid(hash);

            return result;
        }
    }

    /// <summary>
    /// Returns the networked node (if any) that resides beneith this node parent
    /// </summary>
    public static NetworkedNode GetNetNode(this Node parent)
    {
        try
        {
            return FindChild<NetworkedNode>(parent);
        }
        catch (Exception e)
        {
            GD.PrintErr(parent.Name + " Node does NOT contain a NetworkedNode");
            GD.PrintErr(e.Message);

            return null;
        }

    }

    public static T FindParent<T>(this Node Child) where T : Node
    {
        Node result = null;

        if (Child.GetParent() == null) return null;

        if (Child.GetParent() is T) return Child.GetParent() as T;

        else result = FindParent<T>(Child.GetParent());

        return result as T;

    }
    public static T FindChild<T>(this Node Parent) where T : Node
    {
        Node result = null;
        foreach (Node child in Parent.GetChildren())
        {
            if (child is T)
            {

                return child as T;
            }
            else
            {
                result = FindChild<T>(child);
                if (result != null) return result as T;
            }
        }

        return result as T;
    }
    public static Type GetUnderlyingType(MemberInfo member)
    {
        switch (member.MemberType)
        {
            case MemberTypes.Field:
                return ((FieldInfo)member).FieldType;
            case MemberTypes.Property:
                return ((PropertyInfo)member).PropertyType;
            default:
                return null;
        }
    }
    public static object GetValue(this MemberInfo memberInfo, object forObject)
    {
        switch (memberInfo.MemberType)
        {
            case MemberTypes.Field:
                return ((FieldInfo)memberInfo).GetValue(forObject);
            case MemberTypes.Property:
                return ((PropertyInfo)memberInfo).GetValue(forObject);
            default:
                throw new NotImplementedException();
        }
    }
    public static void SetValue(this MemberInfo memberInfo, object forObject, object value)
    {
        switch (memberInfo.MemberType)
        {
            case MemberTypes.Field:
                ((FieldInfo)memberInfo).SetValue(forObject, value);
                break;
            case MemberTypes.Property:
                ((PropertyInfo)memberInfo).SetValue(forObject, value);
                break;
            default:
                throw new NotImplementedException();
        }
    }
    // public static void LoadDataToMembers(IStateObject ObjectInstance, NetObjectState_t State, MemberInfo field, uint tick)
    // {

    //     string objectString = ObjectInstance.GetType().ToString();

    //     List<string> syncProperties = (Attribute.GetCustomAttribute(field, typeof(SyncProperty)) as SyncProperty).FieldNames;

    //     try
    //     {
    //         var fieldInstance = field.GetValue(ObjectInstance);

    //         //just grab the raw value
    //         if (syncProperties.Count == 0)
    //         {
    //             var OldValue = field.GetValue(ObjectInstance);

    //             Type underlyingType = GetUnderlyingType(field);

    //             var NewValue = JsonConvert.DeserializeObject(State.SyncMembers[objectString][field.Name][0], underlyingType);

    //             if (OldValue != NewValue)
    //             {
    //                 field.SetValue(ObjectInstance, NewValue);
    //             }


    //         }
    //         else
    //         {
    //             //the attrbiutes are inside the member instance and not the member instance itself
    //             foreach (var item in syncProperties)
    //             {
    //                 MemberInfo insetFieldInstance = fieldInstance.GetType().GetMember(item)[0];

    //                 var OldValue = insetFieldInstance.GetValue(fieldInstance);

    //                 Type underlyingType = GetUnderlyingType(insetFieldInstance);

    //                 var NewValue = JsonConvert.DeserializeObject(State.SyncMembers[objectString][field.Name][syncProperties.IndexOf(item)], underlyingType);

    //                 if (OldValue != NewValue)
    //                 {
    //                     insetFieldInstance.SetValue(fieldInstance, NewValue);
    //                 }
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         GD.PrintErr(field.Name + " Error Setting Field: " + ex.Message);
    //     }


    // }
    // public static void SaveDataToState(IStateObject ObjectInstance, NetObjectState_t State, MemberInfo field)
    // {

    //     string objectString = ObjectInstance.GetType().ToString();

    //     //add the objects with members to sync
    //     State.SyncMembers.TryAdd(objectString, new());

    //     List<string> syncProperties = (Attribute.GetCustomAttribute(field, typeof(SyncProperty)) as SyncProperty).FieldNames;


    //     State.SyncMembers[objectString].Add(field.Name, new List<string>());

    //     try
    //     {
    //         object fieldValue = field.GetValue(ObjectInstance);

    //         //just grab the raw value
    //         if (syncProperties.Count == 0)
    //         {
    //             State.SyncMembers[objectString][field.Name].Add(JsonConvert.SerializeObject(fieldValue));
    //         }
    //         else
    //         {
    //             //other attributes, modify them inside the field
    //             foreach (var item in syncProperties)
    //             {
    //                 MemberInfo insetFieldMember = fieldValue.GetType().GetMember(item)[0];
    //                 object insetFieldValue = insetFieldMember.GetValue(fieldValue);

    //                 State.SyncMembers[objectString][field.Name].Add(JsonConvert.SerializeObject(insetFieldValue));
    //             }
    //         }




    //     }
    //     catch (Exception e)
    //     {
    //         GD.PrintErr("Error Grabbing Fields: " + ObjectInstance.GetType() + " -> " + field.Name + " | Property Count: " + syncProperties.Count + " \n\n" + e.Message);
    //     }




    // }
}
