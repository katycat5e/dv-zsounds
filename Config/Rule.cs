using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityModManagerNet;

namespace DvMod.ZSounds.Config
{
    public enum RuleType
    {
        Unknown,
        AllOf,
        OneOf,
        If,
        Sound,
        Ref,
    }

    public interface IRule
    {
        public abstract void Apply(Config config, TrainCar car, SoundSet soundSet);
        public abstract void Validate(Config config);
    }

    public static class Rule
    {
        public static IRule Parse(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.String:
                    return new RefRule(token.Value<string>());

                case JTokenType.Object:
                    RuleType type = (RuleType)Enum.Parse(
                        typeof(RuleType),
                        token["type"].Value<string>(),
                        ignoreCase: true);

                    return type switch
                    {
                        RuleType.AllOf => AllOfRule.Parse(token),
                        RuleType.OneOf => OneOfRule.Parse(token),
                        RuleType.If => IfRule.Parse(token),
                        RuleType.Sound => SoundRule.Parse(token),
                        RuleType.Ref => RefRule.Parse(token),

                        _ => throw new Exception($"Unknown rule type {type}"),
                    };

                default:
                    throw new ArgumentException($"Found {token.Type} where a rule was expected");
            }
        }
    }

    public class AllOfRule : IRule
    {
        public readonly IRule[] rules;

        public AllOfRule(IRule[]? rules)
        {
            this.rules = rules ?? new IRule[0];
        }

        public static AllOfRule Parse(JToken token)
        {
            var rules = token["rules"]?.Select(Rule.Parse) ?? Enumerable.Empty<IRule>();
            var soundRules = token["sounds"]?.Select(t => new SoundRule(t.Value<string>())) ?? Enumerable.Empty<SoundRule>();
            return new AllOfRule(rules.Concat(soundRules).ToArray());
        }

        public void Apply(Config config, TrainCar car, SoundSet soundSet)
        {
            foreach (var rule in rules)
                rule.Apply(config, car, soundSet);
        }

        public void Validate(Config config)
        {
            foreach (var rule in rules)
                rule.Validate(config);
        }

        public override string ToString()
        {
            return $"AllOf:\n{string.Join<IRule>("\n", rules).Indent(2)}";
        }
    }

    public class OneOfRule : IRule
    {
        public readonly IRule[] rules;
        public readonly float[] weights;

        public readonly float[] thresholds;

        public OneOfRule(IRule[]? rules, float[]? weights)
        {
            if (rules == null || rules.Length == 0)
                throw new ArgumentException("OneOf rule requires at least one sub-rule");
            if (weights == null)
                weights = Enumerable.Repeat(1f, rules.Length).ToArray();
            if (weights.Length != rules.Length)
                throw new ArgumentException($"Found {weights.Length} weights for {rules.Length} rules in OneOf rule");

            this.rules = rules;
            this.weights = weights;
            thresholds = new float[rules.Length];
            if (thresholds.Length == 0)
                return;

            var totalWeight = weights.Sum();

            thresholds[0] = weights[0] / totalWeight;
            for (int i = 1; i < rules.Length; i++)
                thresholds[i] = thresholds[i - 1] + (weights[i] / totalWeight);
        }

        public static OneOfRule Parse(JToken token)
        {
            return new OneOfRule(
                token["rules"]?.Select(Rule.Parse)?.ToArray(),
                token["weights"]?.Select(t => t.Value<float>())?.ToArray());
        }

        public void Apply(Config config, TrainCar car, SoundSet soundSet)
        {
            var r = UnityEngine.Random.value;
            var index = Array.FindIndex(thresholds, t => r <= t);
            // Main.DebugLog(() => $"weights={string.Join(",",weights)},thresholds={string.Join(",",thresholds)},randomValue={r},index={index}");
            if (index >= 0)
                rules[index].Apply(config, car, soundSet);
        }

        public void Validate(Config config)
        {
            foreach (var rule in rules)
                rule.Validate(config);
        }

        public override string ToString()
        {
            var totalWeight = weights.Sum();
            return $"OneOf:\n{string.Join("\n", rules.Zip(weights, (r, w) => $"{w}/{totalWeight}: {r}")).Indent(2)}";
        }
    }

    public class IfRule : IRule
    {
        public enum IfRuleProperty
        {
            Unknown,
            CarType,
            SkinName,
        }

        private readonly IfRuleProperty property;
        private readonly string value;
        private readonly IRule rule;

        public IfRule(IfRuleProperty property, string value, IRule rule)
        {
            this.property = property;
            this.value = value;
            this.rule = rule;
        }

        public static IfRule Parse(JToken jValue)
        {
            return new IfRule(
                (IfRuleProperty)Enum.Parse(typeof(IfRuleProperty), jValue["property"].Value<string>(), ignoreCase: true),
                jValue["value"].Value<string>(),
                Rule.Parse(jValue["rule"])
            );
        }

        public void Apply(Config config, TrainCar car, SoundSet soundSet)
        {
            if (Applicable(car))
                rule.Apply(config, car, soundSet);
        }

        private static readonly HashSet<string> knownTrainCarTypes =
            Enum.GetNames(typeof(TrainCarType)).Select(name => name.ToLowerInvariant()).ToHashSet();

        public void Validate(Config config)
        {
            switch (property)
            {
                case IfRuleProperty.CarType:
                    if (!knownTrainCarTypes.Contains(value.ToLowerInvariant()))
                        throw new ArgumentException($"Unknown value for CarType: {value}");
                    break;
                case IfRuleProperty.SkinName:
                    break;
            }
            rule.Validate(config);
        }

        private bool Applicable(TrainCar car)
        {
            return property switch
            {
                IfRuleProperty.CarType => string.Equals(car.carType.ToString(), value, StringComparison.OrdinalIgnoreCase),
                IfRuleProperty.SkinName => string.Equals(GetSkinName(car), value, StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        private static Dictionary<string, string>? carSkins;
        private static Dictionary<string, string>? CarSkins
        {
            get
            {
                if (carSkins != null)
                    return carSkins;

                var mod = UnityModManager.FindMod("SkinManagerMod");
                if (mod == null)
                    return null;
                if (!mod.Active)
                    return null;

                var field = mod.Assembly.GetType("SkinManagerMod.Main").GetField("trainCarState", System.Reflection.BindingFlags.Static);
                carSkins = (Dictionary<string, string>)field.GetValue(null);
                return carSkins;
            }
        }

        private static string? GetSkinName(TrainCar car)
        {
            return CarSkins?[car.CarGUID];
        }

        public override string ToString()
        {
            return $"If {property} = {value}:\n{rule.ToString().Indent(2)}";
        }
    }

    public class RefRule : IRule
    {
        public readonly string name;

        public RefRule(string name)
        {
            this.name = name;
        }

        public void Apply(Config config, TrainCar car, SoundSet soundSet)
        {
            config.rules[name].Apply(config, car, soundSet);
        }

        public void Validate(Config config)
        {
            if (!config.rules.ContainsKey(name))
                throw new ArgumentException($"Reference to unknown rule \"{name}\"");
        }

        public static RefRule Parse(JToken token)
        {
            return new RefRule(token["name"].Value<string>());
        }

        public override string ToString()
        {
            return $"Ref \"{name}\"";
        }
    }

    public class SoundRule : IRule
    {
        public readonly string name;

        public SoundRule(string name)
        {
            this.name = name;
        }

        public void Apply(Config config, TrainCar car, SoundSet soundSet)
        {
            config.sounds[name].Apply(soundSet);
        }

        public void Validate(Config config)
        {
            if (!config.sounds.ContainsKey(name))
                throw new ArgumentException($"Reference to unknown sound \"{name}\"");
        }

        public static SoundRule Parse(JToken token)
        {
            return new SoundRule(token["name"].Value<string>());
        }

        public override string ToString()
        {
            return $"Sound \"{name}\"";
        }
    }
}