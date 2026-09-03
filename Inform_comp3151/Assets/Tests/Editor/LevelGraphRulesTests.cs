using System.Collections.Generic;
using NUnit.Framework;

namespace Inkform.LevelGraph.Tests
{
    /// <summary>
    /// Keeps the rule catalogue and the rules themselves from drifting apart.
    ///
    /// The help panel renders LevelGraphRules, so a wrong entry there is worse than no entry: it tells
    /// a designer a rule means something it does not. These tests make the two impossible to change
    /// independently — at least for the graph layer, which is the half that runs without a project.
    /// </summary>
    public class LevelGraphRulesTests
    {
        /// <summary>
        /// One document per graph rule. Adding a rule without adding a fixture here fails
        /// EveryGraphRuleIsReachable, which is the point: a rule nobody can demonstrate is a rule
        /// nobody has tested.
        /// </summary>
        private static readonly (string code, string graph)[] Fixtures =
        {
            ("A1", "room A @0,0\n"),
            ("A2", "entry A\nroom A @0,0\nroom B @1,0\nroom Island @9,9\nlink A <-> B\n"),
            ("A3", "entry A\nroom A @0,0\nroom B @1,0\nlink A -> B\n"),
            ("A4", "entry A\nroom A @0,0\nlink A <-> Nowhere\n"),
            ("A5", "entry A\nroom A @0,0\nroom B @1,0\nlink A <-> B\nlink A -> B\n"),
            ("A6", "entry A\nroom A @0,0\nroom End @1,0\nlink A -> End\n"),
            ("A7", "entry Hub\nroom Hub @0,0\nroom A @1,0\nroom B @2,0\nroom C @3,0\nroom D @4,0\nroom E @5,0\n"
                 + "link Hub <-> A\nlink Hub <-> B\nlink Hub <-> C\nlink Hub <-> D\nlink Hub <-> E\n"),
            ("A8", "entry A\nroom A @0,0\nroom Trap @1,0\nroom Deeper @2,0\n"
                 + "link A -> Trap\nlink Trap -> Deeper\nlink Deeper -> Trap\n"),
        };

        private static List<Finding> Validate(string graph) =>
            LevelGraphValidator.Validate(LevelGraphParser.Parse(graph));

        [Test]
        public void CatalogueHasNoDuplicateCodes()
        {
            var seen = new HashSet<string>();

            foreach (RuleInfo rule in LevelGraphRules.All)
                Assert.IsTrue(seen.Add(rule.Code), $"duplicate catalogue entry for '{rule.Code}'");
        }

        [Test]
        public void EveryEmittedCodeIsInTheCatalogue()
        {
            foreach ((string code, string graph) in Fixtures)
            {
                foreach (Finding f in Validate(graph))
                {
                    Assert.IsTrue(LevelGraphRules.TryGet(f.Code, out _),
                        $"the validator emitted '{f.Code}' (fixture {code}) but LevelGraphRules has no entry for it — "
                        + "the help panel would show a code it cannot explain");
                }
            }
        }

        [Test]
        public void EmittedSeverityMatchesTheCatalogue()
        {
            foreach ((string code, string graph) in Fixtures)
            {
                foreach (Finding f in Validate(graph))
                {
                    if (!LevelGraphRules.TryGet(f.Code, out RuleInfo rule)) continue;   // covered above

                    Assert.AreEqual(rule.Severity, f.Severity,
                        $"'{f.Code}' is catalogued as {rule.Severity} but the validator emitted {f.Severity} "
                        + $"(fixture {code}) — the help panel would misrepresent how serious it is");
                }
            }
        }

        [Test]
        public void EveryGraphRuleIsReachable()
        {
            var emitted = new HashSet<string>();

            foreach ((string _, string graph) in Fixtures)
            {
                foreach (Finding f in Validate(graph)) emitted.Add(f.Code);
            }

            foreach (RuleInfo rule in LevelGraphRules.InLayer(RuleLayer.Graph))
            {
                if (rule.Code == LevelGraphRules.ParseErrorCode) continue;   // raised by the window, not the validator

                Assert.IsTrue(emitted.Contains(rule.Code),
                    $"'{rule.Code}' is catalogued as a graph rule but no fixture triggers it — "
                    + "either the rule was removed and the entry is stale, or it is untested");
            }
        }

        [Test]
        public void EachFixtureTriggersTheRuleItIsNamedFor()
        {
            foreach ((string code, string graph) in Fixtures)
            {
                bool found = false;
                foreach (Finding f in Validate(graph))
                {
                    if (f.Code == code) { found = true; break; }
                }

                Assert.IsTrue(found, $"fixture for '{code}' no longer triggers it");
            }
        }

        [Test]
        public void CatalogueTitlesAreNonEmpty()
        {
            foreach (RuleInfo rule in LevelGraphRules.All)
                Assert.IsFalse(string.IsNullOrWhiteSpace(rule.Title), $"'{rule.Code}' has no description");
        }
    }
}
