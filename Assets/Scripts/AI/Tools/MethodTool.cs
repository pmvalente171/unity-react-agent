using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AI.Tools
{
    /// <summary>
    /// An <see cref="IAgentTool"/> built by reflection from a method marked with
    /// <see cref="AgentToolAttribute"/>. Normally created through <see cref="ToolSet.RegisterFrom"/>.
    /// </summary>
    public sealed class MethodTool : IAgentTool
    {
        private readonly object _target;
        private readonly Definition _definition;

        public string Name => _definition.Name;
        public string Description => _definition.Description;
        public JObject Parameters => _definition.Schema;

        private MethodTool(object target, Definition definition)
        {
            _target = target;
            _definition = definition;
        }

        /// <summary>One tool per [AgentTool] method on <paramref name="target"/>. Invalid methods are logged and skipped.</summary>
        public static List<MethodTool> CreateAll(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var tools = new List<MethodTool>();
            foreach (var definition in GetDefinitions(target.GetType()))
                tools.Add(new MethodTool(target, definition));
            return tools;
        }

        public async Awaitable<ToolResult> Execute(JObject args, CancellationToken ct)
        {
            if (_target is UnityEngine.Object unityObject && unityObject == null)
                return ToolResult.Fail($"Tool '{Name}' is not available anymore.");

            if (!_definition.TryBindArguments(args, ct, out var values, out var error))
                return ToolResult.Fail(error);

            object returned;
            try
            {
                returned = _definition.Method.Invoke(_definition.Method.IsStatic ? null : _target, values);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw(); // keep the tool's own stack trace
                throw;
            }

            switch (_definition.Returns)
            {
                case ReturnKind.Result: return (ToolResult)returned;
                case ReturnKind.Text: return ToolResult.Ok((string)returned);
                case ReturnKind.AwaitableResult: return await (Awaitable<ToolResult>)returned;
                case ReturnKind.AwaitableText: return ToolResult.Ok(await (Awaitable<string>)returned);
                case ReturnKind.Awaitable:
                    await (Awaitable)returned;
                    return ToolResult.Ok("Done.");
                default: return ToolResult.Ok("Done.");
            }
        }

        // ---- Reflection, done once per type ----

        private enum ReturnKind
        {
            Void,
            Result,
            Text,
            Awaitable,
            AwaitableResult,
            AwaitableText,
        }

        private sealed class Parameter
        {
            public ParameterInfo Info;
            public string JsonName;
            public bool IsCancellationToken;
            public bool Required;
        }

        private sealed class Definition
        {
            public MethodInfo Method;
            public string Name;
            public string Description;
            public JObject Schema;
            public Parameter[] Parameters;
            public ReturnKind Returns;

            public bool TryBindArguments(JObject args, CancellationToken ct, out object[] values, out string error)
            {
                values = new object[Parameters.Length];
                error = null;

                for (var i = 0; i < Parameters.Length; i++)
                {
                    var p = Parameters[i];
                    if (p.IsCancellationToken)
                    {
                        values[i] = ct;
                        continue;
                    }

                    var token = args?[p.JsonName];
                    if (token == null || token.Type == JTokenType.Null)
                    {
                        if (p.Required)
                        {
                            error = $"Missing required argument '{p.JsonName}'.";
                            return false;
                        }

                        values[i] = p.Info.HasDefaultValue ? p.Info.DefaultValue : null;
                        continue;
                    }

                    try
                    {
                        values[i] = token.ToObject(p.Info.ParameterType);
                    }
                    catch (Exception e) when (e is JsonException or ArgumentException or FormatException or InvalidCastException)
                    {
                        error = $"Invalid value for '{p.JsonName}': {e.Message}";
                        return false;
                    }
                }

                return true;
            }
        }

        private static readonly Dictionary<Type, Definition[]> Cache = new();

        private static Definition[] GetDefinitions(Type type)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            var definitions = new List<Definition>();
            var seen = new HashSet<MethodInfo>(); // base definitions, so an override and its base register once
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
            {
                foreach (var method in t.GetMethods(flags))
                {
                    var attribute = method.GetCustomAttribute<AgentToolAttribute>();
                    if (attribute == null || !seen.Add(method.GetBaseDefinition())) continue;

                    try
                    {
                        definitions.Add(Build(method, attribute));
                    }
                    catch (NotSupportedException e)
                    {
                        Debug.LogError($"[AgentTool] {t.Name}.{method.Name} was not registered: {e.Message}");
                    }
                }
            }

            var result = definitions.ToArray();
            Cache[type] = result;
            return result;
        }

        private static Definition Build(MethodInfo method, AgentToolAttribute attribute)
        {
            if (method.IsGenericMethodDefinition)
                throw new NotSupportedException("generic methods can't be tools.");

            var properties = new JObject();
            var required = new JArray();
            var parameters = new List<Parameter>();

            foreach (var info in method.GetParameters())
            {
                if (info.ParameterType == typeof(CancellationToken))
                {
                    parameters.Add(new Parameter { Info = info, IsCancellationToken = true });
                    continue;
                }

                if (info.ParameterType.IsByRef)
                    throw new NotSupportedException($"parameter '{info.Name}' is ref/out/in, which isn't supported.");

                var schema = SchemaFor(info.ParameterType, info.Name);
                var description = info.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrEmpty(description)) schema["description"] = description;

                var jsonName = ToSnakeCase(info.Name);
                var isRequired = !info.HasDefaultValue && Nullable.GetUnderlyingType(info.ParameterType) == null;

                properties[jsonName] = schema;
                if (isRequired) required.Add(jsonName);
                parameters.Add(new Parameter { Info = info, JsonName = jsonName, Required = isRequired });
            }

            var toolSchema = new JObject { ["type"] = "object", ["properties"] = properties };
            if (required.Count > 0) toolSchema["required"] = required;

            return new Definition
            {
                Method = method,
                Name = string.IsNullOrWhiteSpace(attribute.Name) ? ToSnakeCase(StripAsync(method.Name)) : attribute.Name,
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Schema = toolSchema,
                Parameters = parameters.ToArray(),
                Returns = ReturnKindOf(method.ReturnType),
            };
        }

        private static JObject SchemaFor(Type type, string parameterName)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string)) return Of("string");
            if (type == typeof(bool)) return Of("boolean");
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
                type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
                return Of("integer");
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return Of("number");
            if (type.IsEnum) return new JObject { ["type"] = "string", ["enum"] = new JArray(Enum.GetNames(type)) };
            if (type == typeof(JObject)) return Of("object");

            var element = type.IsArray ? type.GetElementType()
                : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0]
                : null;
            if (element != null)
                return new JObject { ["type"] = "array", ["items"] = SchemaFor(element, parameterName) };

            throw new NotSupportedException(
                $"parameter '{parameterName}' has type {type.Name}, which isn't supported. " +
                "Use simple types, or a DelegateTool for complex arguments.");

            static JObject Of(string jsonType) => new() { ["type"] = jsonType };
        }

        private static ReturnKind ReturnKindOf(Type type)
        {
            if (type == typeof(void)) return ReturnKind.Void;
            if (type == typeof(ToolResult)) return ReturnKind.Result;
            if (type == typeof(string)) return ReturnKind.Text;
            if (type == typeof(Awaitable)) return ReturnKind.Awaitable;
            if (type == typeof(Awaitable<ToolResult>)) return ReturnKind.AwaitableResult;
            if (type == typeof(Awaitable<string>)) return ReturnKind.AwaitableText;

            throw new NotSupportedException(
                $"return type {type.Name} isn't supported. Use ToolResult, string, void, or their Awaitable versions.");
        }

        private static string StripAsync(string name) =>
            name.Length > 5 && name.EndsWith("Async", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;

        private static string ToSnakeCase(string name)
        {
            var snake = Regex.Replace(name, "([a-z0-9])([A-Z])", "$1_$2");
            snake = Regex.Replace(snake, "([A-Z]+)([A-Z][a-z])", "$1_$2");
            return snake.ToLowerInvariant();
        }
    }
}
