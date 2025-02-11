﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HooahUtility;
using HooahUtility.Model;
using HooahUtility.Serialization.Formatter;
using HooahUtility.Serialization.StudioReference;
using MessagePack;
using UnityEngine;

namespace Utility
{
    public static class SerializationUtility
    {
        private static MethodInfo _serialize;
        private static MethodInfo _deserialize;

        static SerializationUtility()
        {
            foreach (var method in typeof(MessagePackSerializer).GetMethods())
            {
                switch (method.Name)
                {
                    case "Deserialize":
                        var parameters1 = method.GetParameters();
                        if (parameters1.Length == 2 && parameters1[0].ParameterType == typeof(byte[]) &&
                            parameters1[1].ParameterType == typeof(IFormatterResolver))
                        {
                            _deserialize = method;
                        }

                        break;
                    case "Serialize":
                        var parameters2 = method.GetParameters();
                        if (parameters2.Length == 2 && parameters2[1].ParameterType == typeof(IFormatterResolver) &&
                            method.ReturnType == typeof(byte[]))
                        {
                            _serialize = method;
                        }

                        break;
                }
            }
        }

        public static IFormData GetSerializableComponent(GameObject gameObject) =>
            gameObject.GetComponent<IFormData>() ?? gameObject.GetComponentInChildren<IFormData>();

        // ReSharper disable once CognitiveComplexity
        // boost my ego
        public static Dictionary<object, MemberInfo> GetAllSerializableFields<T>(T component) where T : IFormData
        {
            if (component == null) return null;
            var fields = new Dictionary<object, MemberInfo>();

            foreach (var memberInfo in component.GetType().GetMembers())
            {
                var customAttribute = memberInfo.GetCustomAttribute<KeyAttribute>();
                if (customAttribute == null) continue;
                switch (memberInfo)
                {
                    case PropertyInfo propertyInfo when propertyInfo.CanRead && propertyInfo.CanWrite:
                    case FieldInfo _:
                        var intKey = customAttribute.IntKey;
                        if (intKey.HasValue) fields[intKey.Value.ToString()] = memberInfo;
                        if (!ReferenceEquals(null, customAttribute.StringKey) && customAttribute.StringKey.Length >= 0)
                            fields[customAttribute.StringKey] = memberInfo;
                        break;
                }
            }

            return fields;
        }

        public static Dictionary<Type, MethodInfo> SerializationMethodInfo = new Dictionary<Type, MethodInfo>();
        public static Dictionary<Type, MethodInfo> DeserializationMethodInfo = new Dictionary<Type, MethodInfo>();

        public static byte[] Serialize(Type type, object value)
        {
            var parameters = new[] { value, UnityHackResolver.Instance };
            if (SerializationMethodInfo.TryGetValue(type, out var method))
                return (byte[])method.Invoke(null, parameters);

            method = _serialize.MakeGenericMethod(type);
            SerializationMethodInfo[type] = method;
            return (byte[])method.Invoke(null, parameters);
        }

        public static object Deserialize(Type type, byte[] bytes)
        {
            var parameters = new object[] { bytes, UnityHackResolver.Instance };
            if (DeserializationMethodInfo.TryGetValue(type, out var method))
                return method.Invoke(null, parameters);

            method = _deserialize.MakeGenericMethod(type);
            DeserializationMethodInfo[type] = method;
            return method.Invoke(null, parameters);
        }


        public static Dictionary<object, object> GetSerializedData<T>(T component) where T : IFormData =>
            component == null
                ? null
                : GetAllSerializableFields(component).Select(x =>
                {
                    switch (x.Value)
                    {
                        case FieldInfo fieldInfo:
                            // if field have some special shits then ...
                            return new KeyValuePair<object, object>(x.Key,
                                Serialize(fieldInfo.FieldType, fieldInfo.GetValue(component)));
                        case PropertyInfo propertyInfo:
                            return new KeyValuePair<object, object>(x.Key,
                                Serialize(propertyInfo.PropertyType, propertyInfo.GetValue(component)));
                        default:
                            return new KeyValuePair<object, object>(x.Key, null);
                    }
                }).ToDictionary(x => x.Key, x => x.Value);

        public static byte[] GetSerializedBytes<T>(T component) where T : IFormData => component == null
            ? null
            : MessagePackSerializer.Serialize(
                GetSerializedData(component),
                UnityHackResolver.Instance
            );

        public static void DeserializeAndApply<T>(T component, byte[] bytes, int version) where T : IFormData
        {
            if (component == null) return;
            var serializableFields = GetAllSerializableFields(component);

            foreach (var keyValuePair in MessagePackSerializer.Deserialize<Dictionary<object, object>>(bytes,
                UnityHackResolver.Instance))
            {
                if (!serializableFields.TryGetValue(keyValuePair.Key, out var memberInfo)) continue;

                try
                {
                    if (version == 0)
                    {
                        switch (memberInfo)
                        {
                            case FieldInfo fieldInfo:
                                fieldInfo.SetValue(component, keyValuePair.Value);
                                break;
                            case PropertyInfo propertyInfo:
                                propertyInfo.SetValue(component, keyValuePair.Value);
                                break;
                        }
                    }
                    else
                    {
                        var valueBytes = keyValuePair.Value as byte[];

                        switch (memberInfo)
                        {
                            case FieldInfo fieldInfo:
                                fieldInfo.SetValue(component, Deserialize(fieldInfo.FieldType, valueBytes));
                                break;
                            case PropertyInfo propertyInfo:
                                propertyInfo.SetValue(component, Deserialize(propertyInfo.PropertyType, valueBytes));
                                break;
                        }
                    }
                }
                catch (Exception e)
                {
                    var msg =
                        $"Failed to deserialize some data from field {component.GetType().Name}::{memberInfo.Name}.";
#if AI || HS2
                    // todo: Somehow this exception handling cannot catch the reflection and serialization exceptions. which is resulting some sort of strange mess ups. it's working for now at least.
                    HooahUtilityPlugin.Instance.Log.LogError(e);
                    HooahUtilityPlugin.Instance.Log.LogMessage(msg);
#else
                    Debug.LogError(e);
                    Debug.LogWarning(msg);
#endif
                }
            }
        }

        public static bool TryCastMember<T>(this MemberInfo memberInfo, object reference, out T chaRef) where T : class
        {
            chaRef = default(T);

            switch (memberInfo)
            {
                case FieldInfo fieldInfo:
                    chaRef = fieldInfo.GetValue(reference) as T;
                    break;
                case PropertyInfo propertyInfo:
                    chaRef = propertyInfo.GetValue(reference) as T;
                    break;
                default:
                    return false;
            }

            return (chaRef != null);
        }

        public static bool TryGetMemberValue(MemberInfo memberInfo, object instance, out object value)
        {
            switch (memberInfo)
            {
                case PropertyInfo propertyInfo:
                    value = propertyInfo.GetValue(instance);
                    return true;
                case FieldInfo fieldInfo:
                    value = fieldInfo.GetValue(instance);
                    return true;
                default:
                    value = default;
                    return false;
            }
        }

        public static void TrySetMemberValue(MemberInfo memberInfo, object instance, object value)
        {
            switch (memberInfo)
            {
                case PropertyInfo propertyInfo:
                    propertyInfo.SetValue(instance, value);
                    return;
                case FieldInfo fieldInfo:
                    fieldInfo.SetValue(instance, value);
                    return;
                default:
                    return;
            }
        }
    }
}