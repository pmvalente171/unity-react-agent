using Newtonsoft.Json.Linq;

namespace AI.Tools
{
    /// <summary>
    /// Small builder for tool argument schemas, so tools don't need hand-written JSON:
    /// <code>
    /// ToolSchema.Object()
    ///     .Property("place", "string", "Where to go", enumValues: new[] { "kitchen", "hall" })
    ///     .Property("run", "boolean", "Run instead of walk", required: false)
    ///     .Build();
    /// </code>
    /// For anything more complex, use <see cref="Parse"/> or build a JObject directly.
    /// </summary>
    public class ToolSchema
    {
        private readonly JObject _properties = new();
        private readonly JArray _required = new();

        public static ToolSchema Object() => new();

        public static JObject Parse(string json) => JObject.Parse(json);

        /// <param name="type">JSON Schema type: string, number, integer, boolean, array, object.</param>
        public ToolSchema Property(string name, string type, string description = null,
            bool required = true, string[] enumValues = null)
        {
            var property = new JObject { ["type"] = type };
            if (description != null) property["description"] = description;
            if (enumValues != null) property["enum"] = new JArray(enumValues);

            _properties[name] = property;
            if (required) _required.Add(name);
            return this;
        }

        public JObject Build()
        {
            var schema = new JObject { ["type"] = "object", ["properties"] = _properties.DeepClone() };
            if (_required.Count > 0) schema["required"] = _required.DeepClone();
            return schema;
        }
    }
}
