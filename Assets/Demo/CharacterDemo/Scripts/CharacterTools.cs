using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using AI.Tools;
using Demo.Shared;
using UnityEngine;

namespace Demo.CharacterDemo
{
    /// <summary>
    /// The agent's tools for the demo character, as [AgentTool] methods. Thin adapters:
    /// the game logic lives in <see cref="DemoCharacter"/> and <see cref="Landmark"/>.
    /// </summary>
    [RequireComponent(typeof(DemoCharacter))]
    public class CharacterTools : MonoBehaviour
    {
        [SerializeField] private ToolSet toolSet;

        private DemoCharacter _character;

        private void Awake() => _character = GetComponent<DemoCharacter>();
        private void OnEnable() => toolSet.RegisterFrom(this);
        private void OnDisable() => toolSet.UnregisterFrom(this);

        [AgentTool, Description("Look around: says where you are and lists every place you can walk to, " +
                                "with its distance and direction (north is up on the map).")]
        private string LookAround()
        {
            var position = transform.position;
            var places = Landmark.All.OrderBy(l => l.FlatDistance(position)).ToList();
            var here = places.FirstOrDefault(l => l.Contains(position));

            var text = new StringBuilder();
            text.AppendLine(here != null
                ? $"You are in the {here.PlaceName}. {here.Description}"
                : "You are between places.");

            text.AppendLine("Places:");
            foreach (var place in places)
            {
                if (place == here) continue;
                text.AppendLine($"- {place.PlaceName}: {place.FlatDistance(position):F0} m " +
                                $"{Compass(place.transform.position - position)}. {place.Description}");
            }

            return text.ToString().TrimEnd();
        }

        [AgentTool, Description("Walk to a named place. Takes a few seconds. Use look_around to see the place names.")]
        private async Awaitable<ToolResult> MoveTo(
            [Description("Name of the place, e.g. \"kitchen\".")] string place,
            CancellationToken ct)
        {
            var target = Landmark.Find(place);
            if (target == null)
                return ToolResult.Fail($"Unknown place '{place}'. Known places: " +
                                       string.Join(", ", Landmark.All.Select(l => l.PlaceName)) + ".");

            if (target.Contains(transform.position))
                return ToolResult.Ok($"You are already in the {target.PlaceName}.");

            return (await _character.MoveTo(target.transform.position, ct)) switch
            {
                MoveResult.Arrived => ToolResult.Ok($"You arrived at the {target.PlaceName}."),
                MoveResult.Unreachable => ToolResult.Fail($"There is no path to the {target.PlaceName}."),
                _ => ToolResult.Fail($"You couldn't reach the {target.PlaceName} in time."),
            };
        }

        [AgentTool, Description("Say something out loud. It appears in a speech bubble above you.")]
        private string Say([Description("What to say. Keep it short.")] string text)
        {
            _character.Say(text);
            return $"You said: \"{text}\"";
        }

        private static string Compass(Vector3 direction)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            var angle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg; // 0 = +Z = north
            var index = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / 45f) % 8;
            return $"to the {names[index]}";
        }
    }
}
