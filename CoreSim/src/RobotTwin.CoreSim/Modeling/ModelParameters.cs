using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[param] A named value of a model ("genislik = 60", "delik_x = genislik / 2 - 10").</summary>
    public sealed class ModelParameter
    {
        public string Name { get; set; } = "";

        /// <summary>A number or a formula of other parameters (Modeling/Expressions.cs).</summary>
        public string Expression { get; set; } = "0";

        public string Comment { get; set; } = "";

        /// <summary>What the formula came to when the parameters were last worked out.</summary>
        [JsonIgnore]
        public double Value { get; internal set; }

        /// <summary>Why it could not be worked out, if it could not.</summary>
        [JsonIgnore]
        public string? Error { get; internal set; }

        public ModelParameter Clone() => new ModelParameter { Name = Name, Expression = Expression, Comment = Comment, Value = Value, Error = Error };
    }

    /// <summary>
    /// [param] The model's parameters worked out in the order they need each other (a loop is reported, not followed), and
    /// pushed into what is bound to them: a part's dimensions (<see cref="PrimitiveSpec.Expressions"/>) and a dimensioned
    /// sketch's dimensions (<see cref="SketchConstraint.Expression"/>), whose sketches are then solved again.
    /// </summary>
    public static class ModelParameters
    {
        /// <summary>Works every parameter out; returns the problems (unknown names, loops, bad formulas), empty when none.</summary>
        public static List<string> Evaluate(ModelDocument doc)
        {
            var problems = new List<string>();
            var list = doc.Parameters ?? new List<ModelParameter>();
            var byName = new Dictionary<string, ModelParameter>(StringComparer.Ordinal);
            foreach (var p in list)
            {
                p.Error = null;
                if (!Expression.IsName(p.Name)) p.Error = $"'{p.Name}' ad olamaz";
                else if (byName.ContainsKey(p.Name)) p.Error = $"'{p.Name}' iki kez tanımlı";
                else byName[p.Name] = p;
            }

            var state = new Dictionary<string, int>(StringComparer.Ordinal);   // 1 working out, 2 done
            double? Value(string name, List<string> path)
            {
                if (!byName.TryGetValue(name, out var p)) return null;
                if (state.TryGetValue(name, out int s))
                {
                    if (s == 2) return p.Error == null ? p.Value : (double?)null;
                    p.Error = "döngü: " + string.Join(" → ", path.Concat(new[] { name }));
                    return null;
                }

                state[name] = 1;
                path.Add(name);
                string? inner = null;
                bool ok = Expression.TryEvaluate(p.Expression, n =>
                {
                    double? v = Value(n, path);
                    if (v == null && inner == null) inner = byName.TryGetValue(n, out var q) && q.Error != null ? $"'{n}' çözülemedi" : $"'{n}' tanımlı değil";
                    return v;
                }, out double value, out string? error);
                path.RemoveAt(path.Count - 1);
                state[name] = 2;
                if (p.Error != null) return null;   // a loop through it
                if (!ok)
                {
                    p.Error = inner ?? error;
                    return null;
                }

                p.Value = value;
                return value;
            }

            foreach (var p in list)
                if (p.Error == null && byName.TryGetValue(p.Name, out var same) && ReferenceEquals(same, p))
                    Value(p.Name, new List<string>());
            foreach (var p in list)
                if (p.Error != null) problems.Add($"{p.Name}: {p.Error}");
            return problems;
        }

        /// <summary>The value of a formula with the model's parameters (as last worked out).</summary>
        public static bool TryValue(ModelDocument doc, string formula, out double value, out string? error)
        {
            var map = (doc.Parameters ?? new List<ModelParameter>()).Where(p => p.Error == null)
                .GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
            return Expression.TryEvaluate(formula, n => map.TryGetValue(n, out double v) ? v : (double?)null, out value, out error);
        }

        /// <summary>
        /// Works the parameters out and sets every bound dimension; sketches with bound dimensions are solved again (a
        /// sketch the new values make contradict itself keeps its old values). Returns the problems.
        /// </summary>
        public static List<string> Apply(ModelDocument doc)
        {
            var problems = Evaluate(doc);
            foreach (var o in doc.Objects)
            {
                var spec = o.Primitive;
                if (spec == null) continue;
                if (spec.Expressions != null)
                {
                    foreach (var kv in spec.Expressions)
                    {
                        if (TryValue(doc, kv.Value, out double v, out string? error)) spec.Set(kv.Key, v);
                        else problems.Add($"{o.Name} · {kv.Key}: {error}");
                    }
                }

                var sketch = spec.Sketch?.Constrained;
                if (sketch == null || sketch.Constraints.All(c => string.IsNullOrWhiteSpace(c.Expression))) continue;
                var trial = sketch.Clone();
                foreach (var c in trial.Constraints)
                {
                    if (string.IsNullOrWhiteSpace(c.Expression)) continue;
                    if (TryValue(doc, c.Expression!, out double v, out string? error)) c.Value = v;
                    else problems.Add($"{o.Name} · {c.Id}: {error}");
                }

                if (trial.Solve(explain: false).State == SketchState.Conflicting)
                {
                    problems.Add($"{o.Name}: eskiz bu değerlerle çelişiyor, eski hâlinde kaldı");
                    continue;
                }

                spec.Sketch!.Constrained = trial;
                spec.Sketch.ApplyConstrained();
            }

            return problems;
        }

        /// <summary>Whether anything in the model uses a parameter (so a rename or removal can be warned about).</summary>
        public static bool IsUsed(ModelDocument doc, string name) =>
            (doc.Parameters ?? new List<ModelParameter>()).Any(p => p.Name != name && Expression.Names(p.Expression).Contains(name)) ||
            doc.Objects.Any(o => o.Primitive?.Expressions?.Values.Any(e => Expression.Names(e).Contains(name)) == true ||
                                 o.Primitive?.Sketch?.Constrained?.Constraints.Any(c => c.Expression != null && Expression.Names(c.Expression).Contains(name)) == true);
    }
}
