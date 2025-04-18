// Copyright 2016-2019, Pulumi Corporation

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Enum = System.Enum;
using Type = System.Type;

// ReSharper disable TailRecursiveCall

namespace Pulumi.Serialization
{
    internal static class Converter
    {
        public static OutputData<T> ConvertValue<T>(Action<string> warn, string context, Value value)
        {
            var (data, isKnown, isSecret) = ConvertValue(warn, context, value, typeof(T));
            var result = data == null ? default(T)! : (T)data;
            return new OutputData<T>(ImmutableHashSet<Resource>.Empty, result!, isKnown, isSecret);
        }

        public static OutputData<object?> ConvertValue(Action<string> warn, string context, Value value, Type targetType)
        {
            return ConvertValue(warn, context, value, targetType, ImmutableHashSet<Resource>.Empty);
        }

        public static OutputData<object?> ConvertValue(
            Action<string> warn, string context, Value value, Type targetType, ImmutableHashSet<Resource> resources)
        {
            CheckTargetType(context, targetType, new HashSet<Type>());

            var (deserialized, isKnown, isSecret) = Deserializer.Deserialize(value);
            var converted = ConvertObject(warn, context, deserialized, targetType);

            return new OutputData<object?>(resources, converted, isKnown, isSecret);
        }

        private static object? ConvertObject(Action<string> warn, string context, object? val, Type targetType)
        {
            var (result, error) = TryConvertObject(warn, context, val, targetType);
            if (error != null)
            {
                warn(error);
            }

            return result;
        }

        private static (object?, string?) TryConvertObject(Action<string> warn, string context, object? val, Type targetType)
        {
            var targetIsNullable = targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>);

            // Note: 'null's can enter the system as the representation of an 'unknown' value.
            // Before calling 'Convert' we will have already lifted the 'IsKnown' bit out, but we
            // will be passing null around as a value.
            if (val == null)
            {
                if (targetIsNullable)
                {
                    // A 'null' value coerces to a nullable null.
                    return (null, null);
                }

                if (targetType.IsValueType)
                {
                    return (Activator.CreateInstance(targetType), null);
                }

                // for all other types, can just return the null value right back out as a legal
                // reference type value.
                return (null, null);
            }

            // We're not null and we're converting to Nullable<T>, just convert our value to be a T.
            if (targetIsNullable)
                return TryConvertObject(warn, context, val, targetType.GenericTypeArguments.Single());

            if (targetType == typeof(string))
                return TryEnsureType<string>(context, val);

            if (targetType == typeof(bool))
                return TryEnsureType<bool>(context, val);

            if (targetType == typeof(double))
                return TryEnsureType<double>(context, val);

            if (targetType == typeof(int))
            {
                var (d, exception) = TryEnsureType<double>(context, val);
                if (exception != null)
                    return (null, exception);

                return ((int)d, exception);
            }

            if (targetType == typeof(object))
                return (val, null);

            if (targetType == typeof(Asset))
            {
                var (d, exception) = TryEnsureType<Asset>(context, val);
                if (exception != null)
                {
                    d = new InvalidAsset();
                }
                return (d, null);
            }

            if (targetType == typeof(Archive))
            {
                var (d, exception) = TryEnsureType<Archive>(context, val);
                if (exception != null)
                {
                    d = new InvalidArchive();
                }
                return (d, null);
            }

            if (targetType == typeof(AssetOrArchive))
            {
                var (d, exception) = TryEnsureType<AssetOrArchive>(context, val);
                if (exception != null)
                {
                    d = new InvalidAsset();
                }
                return (d, null);
            }

            if (targetType == typeof(JsonElement))
                return TryConvertJsonElement(context, val);

            if (targetType.IsSubclassOf(typeof(Resource)) || targetType == typeof(Resource))
                return TryEnsureType<Resource>(context, val);

            if (targetType.IsEnum)
            {
                var underlyingType = targetType.GetEnumUnderlyingType();
                var (value, exception) = TryConvertObject(warn, context, val, underlyingType);
                if (exception != null || value is null)
                    return (null, exception);

                return (Enum.ToObject(targetType, value), null);
            }

            if (targetType.IsValueType && targetType.GetCustomAttribute<EnumTypeAttribute>() != null)
            {
                var valType = val.GetType();
                if (valType != typeof(string) &&
                    valType != typeof(double))
                {
                    return (null,
                        $"Expected {typeof(string).FullName} or {typeof(double).FullName} but got {valType.FullName} deserializing {context}");
                }

                var enumTypeConstructor = targetType.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { valType }, null);
                if (enumTypeConstructor == null)
                {
                    return (null,
                        $"Expected target type {targetType.FullName} to have a constructor with a single {valType.FullName} parameter.");
                }
                return (enumTypeConstructor.Invoke(new[] { val }), null);
            }

            if (targetType.IsConstructedGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(Union<,>))
                    return TryConvertOneOf(warn, context, val, targetType);

                if (targetType.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
                    return TryConvertArray(warn, context, val, targetType);

                if (targetType.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>))
                    return TryConvertDictionary(warn, context, val, targetType);

                if (targetType.GetGenericTypeDefinition() == typeof(Output<>))
                    return ((object?, string?))TryConvertOutputMethod
                        .MakeGenericMethod(targetType.GetGenericArguments().Single())
                        .Invoke(null, new object?[] { warn, context, val, targetType })!;

                throw new InvalidOperationException(
                    $"Unexpected generic target type {targetType.FullName} when deserializing {context}");
            }

            if (targetType.GetCustomAttribute<OutputTypeAttribute>() == null)
                return (null, $"Unexpected target type {targetType.FullName} when deserializing {context}");

            var constructor = GetPropertyConstructor(targetType);
            if (constructor == null)
                return (null,
                    $"Expected target type {targetType.FullName} to have [{nameof(OutputConstructorAttribute)}] constructor when deserializing {context}");

            var (dictionary, tempException) = TryEnsureType<ImmutableDictionary<string, object>>(context, val);
            if (tempException != null)
                return (null, tempException);

            var constructorParameters = constructor.GetParameters();
            var arguments = new object?[constructorParameters.Length];

            for (int i = 0, n = constructorParameters.Length; i < n; i++)
            {
                var parameter = constructorParameters[i];
                var parameterName = parameter.Name!;
                var attribute = parameter.GetCustomAttribute<OutputConstructorParameterAttribute>();
                if (attribute != null)
                {
                    parameterName = attribute.Name;
                }

                // Note: TryGetValue may not find a value here.  That can happen for things like
                // unknown vals.  That's ok.  We'll pass that through to 'Convert' and will get the
                // default value needed for the parameter type.
                dictionary!.TryGetValue(parameterName, out var argValue);

                arguments[i] = ConvertObject(warn, $"{targetType.FullName}({parameter.Name})", argValue, parameter.ParameterType);
            }

            return (constructor.Invoke(arguments), null);
        }

        private static (object?, string?) TryConvertJsonElement(
            string context, object val)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                var error = TryWriteJson(context, writer, val);
                if (error != null)
                    return (null, error);
            }

            stream.Position = 0;
            var document = JsonDocument.Parse(stream);
            var element = document.RootElement;
            return (element, null);
        }

        private static string? TryWriteJson(string context, Utf8JsonWriter writer, object? val)
        {
            switch (val)
            {
                case string v:
                    writer.WriteStringValue(v);
                    return null;
                case double v:
                    writer.WriteNumberValue(v);
                    return null;
                case bool v:
                    writer.WriteBooleanValue(v);
                    return null;
                case null:
                    writer.WriteNullValue();
                    return null;
                case ImmutableArray<object?> v:
                    writer.WriteStartArray();
                    foreach (var element in v)
                    {
                        var exception = TryWriteJson(context, writer, element);
                        if (exception != null)
                            return exception;
                    }
                    writer.WriteEndArray();
                    return null;
                case ImmutableDictionary<string, object?> v:
                    writer.WriteStartObject();
                    foreach (var (key, element) in v)
                    {
                        writer.WritePropertyName(key);
                        var exception = TryWriteJson(context, writer, element);
                        if (exception != null)
                            return exception;
                    }
                    writer.WriteEndObject();
                    return null;
                case IOutput output:
                    var outputData = output.GetDataAsync().GetAwaiter().GetResult();
                    return TryWriteJson(context, writer, outputData.Value);
                default:
                    return $"Unexpected type {val.GetType().FullName} when converting {context} to {nameof(JsonElement)}";
            }
        }

        private static (T, string?) TryEnsureType<T>(string context, object val)
            => val is T t ? (t, null) : (default(T)!, $"Expected {typeof(T).FullName} but got {val.GetType().FullName} deserializing {context}");

        private static (object?, string?) TryConvertOneOf(Action<string> warn, string context, object val, Type oneOfType)
        {
            var firstType = oneOfType.GenericTypeArguments[0];
            var secondType = oneOfType.GenericTypeArguments[1];

            var (val1, exception1) = TryConvertObject(warn, $"{context}.AsT0", val, firstType);
            if (exception1 == null)
            {
                var fromT0Method = oneOfType.GetMethod(nameof(Union<int, int>.FromT0), BindingFlags.Public | BindingFlags.Static);
                return (fromT0Method?.Invoke(null, new[] { val1 }), null);
            }

            var (val2, exception2) = TryConvertObject(warn, $"{context}.AsT1", val, secondType);
            if (exception2 == null)
            {
                var fromT1Method = oneOfType.GetMethod(nameof(Union<int, int>.FromT1), BindingFlags.Public | BindingFlags.Static);
                return (fromT1Method?.Invoke(null, new[] { val2 }), null);
            }

            return (null, $"Expected {firstType.FullName} or {secondType.FullName} but got {val.GetType().FullName} deserializing {context}");
        }

        private static (object?, string?) TryConvertArray(
            Action<string> warn,
            string fieldName, object val, Type targetType)
        {
            if (!(val is ImmutableArray<object> array))
                return (null,
                    $"Expected {typeof(ImmutableArray<object>).FullName} but got {val.GetType().FullName} deserializing {fieldName}");

            var builder =
                typeof(ImmutableArray).GetMethod(nameof(ImmutableArray.CreateBuilder), Array.Empty<Type>())!
                                      .MakeGenericMethod(targetType.GenericTypeArguments)
                                      .Invoke(obj: null, parameters: null)!;

            var builderAdd = builder.GetType().GetMethod(nameof(ImmutableArray<int>.Builder.Add))!;
            var builderToImmutable = builder.GetType().GetMethod(nameof(ImmutableArray<int>.Builder.ToImmutable))!;

            var elementType = targetType.GenericTypeArguments.Single();
            foreach (var element in array)
            {
                var e = ConvertObject(warn, fieldName, element, elementType);

                builderAdd.Invoke(builder, new[] { e });
            }

            return (builderToImmutable.Invoke(builder, null), null);
        }

        private static readonly MethodInfo TryConvertOutputMethod = typeof(Converter).GetMethod(nameof(TryConvertOutput), BindingFlags.NonPublic | BindingFlags.Static)
                                                           ?? throw new MissingMethodException($"Method {nameof(TryConvertOutput)} not found.");

        private static (object?, string?) TryConvertOutput<T>(
            Action<string> warn,
            string fieldName, object val, Type targetType)
        {
            var (element, error) = TryConvertObject(warn, fieldName, val, typeof(T));

            if (error != null)
            {
                return (element, error);
            }

            return (Output.Create((T)element!), null);
        }

        private static (object?, string?) TryConvertDictionary(
            Action<string> warn,
            string fieldName, object val, Type targetType)
        {
            if (!(val is ImmutableDictionary<string, object> dictionary))
                return (null,
                    $"Expected {typeof(ImmutableDictionary<string, object>).FullName} but got {val.GetType().FullName} deserializing {fieldName}");

            // check if already in the form we need.  no need to convert anything.
            if (targetType == typeof(ImmutableDictionary<string, object>))
                return (val, null);

            var keyType = targetType.GenericTypeArguments[0];
            if (keyType != typeof(string))
                return (null,
                    $"Unexpected type {targetType.FullName} when deserializing {fieldName}. ImmutableDictionary's TKey type was not {typeof(string).FullName}");

            var builder =
                typeof(ImmutableDictionary).GetMethod(nameof(ImmutableDictionary.CreateBuilder), Array.Empty<Type>())!
                                           .MakeGenericMethod(targetType.GenericTypeArguments)
                                           .Invoke(obj: null, parameters: null)!;

            var builderAdd = builder.GetType().GetMethod(nameof(ImmutableDictionary<string, object>.Builder.Add), targetType.GenericTypeArguments)!;
            var builderToImmutable = builder.GetType().GetMethod(nameof(ImmutableDictionary<string, object>.Builder.ToImmutable))!;

            var elementType = targetType.GenericTypeArguments[1];
            foreach (var (key, element) in dictionary)
            {
                var e = ConvertObject(warn, fieldName, element, elementType);

                builderAdd.Invoke(builder, new[] { key, e });
            }

            return (builderToImmutable.Invoke(builder, null), null);
        }

        public static void CheckTargetType(string context, Type targetType, HashSet<Type> seenTypes)
        {
            // types can be recursive.  So only dive into a type if it's the first time we're seeing it.
            if (!seenTypes.Add(targetType))
                return;

            if (targetType == typeof(bool) ||
                targetType == typeof(int) ||
                targetType == typeof(double) ||
                targetType == typeof(string) ||
                targetType == typeof(object) ||
                targetType == typeof(Asset) ||
                targetType == typeof(Archive) ||
                targetType == typeof(AssetOrArchive) ||
                targetType == typeof(JsonElement))
            {
                return;
            }

            if (targetType.IsSubclassOf(typeof(Resource)) || targetType == typeof(Resource))
            {
                return;
            }

            if (targetType == typeof(ImmutableDictionary<string, object>))
            {
                // This type is what is generated for things like azure/aws tags.  It's an untyped
                // map in our original schema.  This is the 1st out of 2 places that `object` should
                // appear as a legal value.
                return;
            }

            if (targetType == typeof(ImmutableArray<object>))
            {
                // This type is what is generated for things like YAML decode invocation response
                // in the Kubernetes provider. The elements of the array would typically be
                // immutable dictionaries.  This is the 2nd out of 2 places that `object` should
                // appear as a legal value.
                return;
            }

            if (targetType.IsEnum && targetType.GetEnumUnderlyingType() == typeof(int))
            {
                return;
            }

            if (targetType.IsValueType && targetType.GetCustomAttribute<EnumTypeAttribute>() != null)
            {
                if (CheckEnumType(targetType, typeof(string)) ||
                    CheckEnumType(targetType, typeof(double)))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"{targetType.FullName} had [{nameof(EnumTypeAttribute)}], but did not contain constructor with a single String or Double parameter.");

                static bool CheckEnumType(Type targetType, Type underlyingType)
                {
                    var constructor = targetType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { underlyingType }, null);
                    if (constructor == null)
                    {
                        return false;
                    }

                    var op = targetType.GetMethod("op_Explicit", BindingFlags.Public | BindingFlags.Static, null, new[] { targetType }, null);
                    if (op != null && op.ReturnType == underlyingType)
                    {
                        return true;
                    }

                    throw new InvalidOperationException(
                        $"{targetType.FullName} had [{nameof(EnumTypeAttribute)}], but did not contain an explicit conversion operator to {underlyingType.FullName}.");
                }
            }

            if (targetType.IsConstructedGenericType)
            {
                if (targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    CheckTargetType(context, targetType.GenericTypeArguments.Single(), seenTypes);
                    return;
                }
                if (targetType.GetGenericTypeDefinition() == typeof(Union<,>))
                {
                    CheckTargetType(context, targetType.GenericTypeArguments[0], seenTypes);
                    CheckTargetType(context, targetType.GenericTypeArguments[1], seenTypes);
                    return;
                }
                if (targetType.GetGenericTypeDefinition() == typeof(ImmutableArray<>))
                {
                    CheckTargetType(context, targetType.GenericTypeArguments.Single(), seenTypes);
                    return;
                }
                if (targetType.GetGenericTypeDefinition() == typeof(ImmutableDictionary<,>))
                {
                    var dictTypeArgs = targetType.GenericTypeArguments;
                    if (dictTypeArgs[0] != typeof(string))
                    {
                        throw new InvalidOperationException($@"{context} contains invalid type {targetType.FullName}:
    The only allowed ImmutableDictionary 'TKey' type is 'String'.");
                    }

                    CheckTargetType(context, dictTypeArgs[1], seenTypes);
                    return;
                }
                if (targetType.GetGenericTypeDefinition() == typeof(Output<>))
                {
                    CheckTargetType(context, targetType.GenericTypeArguments.Single(), seenTypes);
                    return;
                }
                throw new InvalidOperationException($@"{context} contains invalid type {targetType.FullName}:
    The only generic types allowed are ImmutableArray<...> and ImmutableDictionary<string, ...>");
            }

            var propertyTypeAttribute = targetType.GetCustomAttribute<OutputTypeAttribute>();
            if (propertyTypeAttribute == null)
            {
                throw new InvalidOperationException(
$@"{context} contains invalid type {targetType.FullName}. Allowed types are:
    String, Boolean, Int32, Double,
    Nullable<...>, ImmutableArray<...> and ImmutableDictionary<string, ...> or
    a class explicitly marked with the [{nameof(OutputTypeAttribute)}].");
            }

            var constructor = GetPropertyConstructor(targetType);
            if (constructor == null)
            {
                throw new InvalidOperationException(
$@"{targetType.FullName} had [{nameof(OutputTypeAttribute)}], but did not contain constructor marked with [{nameof(OutputConstructorAttribute)}].");
            }

            foreach (var param in constructor.GetParameters())
            {
                CheckTargetType($@"{targetType.FullName}({param.Name})", param.ParameterType, seenTypes);
            }
        }

        private static ConstructorInfo? GetPropertyConstructor(Type outputTypeArg)
        {
            var constructors = outputTypeArg.GetConstructors(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            var outputConstructor = constructors.FirstOrDefault(c => c.GetCustomAttribute<OutputConstructorAttribute>() != null);
            if (outputConstructor != null)
            {
                return outputConstructor;
            }

            var publicConstructors = constructors.Where(x => x.IsPublic).ToArray();
            if (publicConstructors.Length == 1)
            {
                return publicConstructors[0];
            }

            return null;
        }
    }
}
