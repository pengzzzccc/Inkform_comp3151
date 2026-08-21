using System.Collections.Generic;
using NUnit.Framework;

namespace Inkform.LevelGraph.Tests
{
    /// <summary>
    /// Graph-rule tests. Each one builds the smallest document that triggers exactly one rule, so a
    /// failure names the broken rule instead of "something about validation".
    ///
    /// The real 22-room graph passes all of these today (I checked: no one-way links, nothing
    /// unreachable, no room over the four-door limit), so these fixtures are deliberately synthetic —
    /// a validator only earns its keep on the graphs nobody has built yet.
    /// </summary>
    public class LevelGraphValidatorTests
    {
        private static LevelGraphDocument Doc(string text) => LevelGraphParser.Parse(text);

        private static bool Has(List<Finding> findings, string code)
        {
            foreach (Finding f in findings)
            {
                if (f.Code == code) return true;
            }
            return false;
        }

        private static int Count(List<Finding> findings, string code)
        {
            int n = 0;
            foreach (Finding f in findings)
            {
                if (f.Code == code) n++;
            }
            return n;
        }

        [Test]
        public void HealthyGraph_ProducesNoWarningsOrErrors()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom B @1,0\nroom C @2,0\n" +
                "link A <-> B\nlink B <-> C\n"));

            foreach (Finding f in findings)
                Assert.AreEqual(Severity.Info, f.Severity, $"unexpected {f.Severity}: {f}");
        }

        [Test]
        public void A1_MissingEntry_IsAnError()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc("room A @0,0\n"));

            Assert.IsTrue(Has(findings, "A1"));
        }

        [Test]
        public void A1_EntryNamingAnUndeclaredRoom_IsAnError()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc("entry Ghost\nroom A @0,0\n"));

            Assert.IsTrue(Has(findings, "A1"));
        }

        [Test]
        public void A2_UnreachableRoom_IsAnError()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom B @1,0\nroom Island @9,9\n" +
                "link A <-> B\n"));

            Assert.IsTrue(Has(findings, "A2"));
            Assert.AreEqual(1, Count(findings, "A2"));
        }

        [Test]
        public void A3_OneWayLink_IsReportedAsInfoNotAWarning()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom B @1,0\n" +
                "link A -> B\nlink B -> A\n"));

            // Both directions exist, just written as two one-way lines, so nothing to report.
            Assert.IsFalse(Has(findings, "A3"));

            List<Finding> oneWay = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom B @1,0\n" +
                "link A -> B\n"));

            Assert.IsTrue(Has(oneWay, "A3"));
            foreach (Finding f in oneWay)
            {
                if (f.Code == "A3") Assert.AreEqual(Severity.Info, f.Severity,
                    "writing -> is how an author declares a deliberate one-way door");
            }
        }

        [Test]
        public void A4_LinkToAnUndeclaredRoom_IsAnError()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\nroom A @0,0\n" +
                "link A <-> Nowhere\n"));

            Assert.IsTrue(Has(findings, "A4"));
        }

        [Test]
        public void A5_DuplicateExitId_IsAnError()
        {
            // Two doors out of A both defaulting to the id "B" — TargetOf only ever finds the first.
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom B @1,0\n" +
                "link A <-> B\nlink A -> B\n"));

            Assert.IsTrue(Has(findings, "A5"));
        }

        [Test]
        public void A6_RoomWithNoExits_IsAWarning()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom End @1,0\n" +
                "link A -> End\n"));

            Assert.IsTrue(Has(findings, "A6"));
        }

        [Test]
        public void A7_MoreExitsThanGreyboxDoorSlots_IsAWarning()
        {
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry Hub\n" +
                "room Hub @0,0\nroom A @1,0\nroom B @2,0\nroom C @3,0\nroom D @4,0\nroom E @5,0\n" +
                "link Hub <-> A\nlink Hub <-> B\nlink Hub <-> C\nlink Hub <-> D\nlink Hub <-> E\n"));

            Assert.IsTrue(Has(findings, "A7"));
        }

        [Test]
        public void A8_RoomYouCannotLeave_IsAWarning()
        {
            // Reachable, has an exit, but every route out leads away from the entry: a softlock.
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom Trap @1,0\nroom Deeper @2,0\n" +
                "link A -> Trap\nlink Trap -> Deeper\nlink Deeper -> Trap\n"));

            Assert.IsTrue(Has(findings, "A8"), "a room with no route back to the entry strands the player");
        }

        [Test]
        public void A8_IsNotReportedForAnUnreachableRoom()
        {
            // An unreachable room is already an A2; also calling it a softlock would be noise.
            List<Finding> findings = LevelGraphValidator.Validate(Doc(
                "entry A\n" +
                "room A @0,0\nroom Island @9,9\n"));

            Assert.IsTrue(Has(findings, "A2"));
            Assert.IsFalse(Has(findings, "A8"));
        }

        [Test]
        public void Validate_NullDocument_ReturnsEmpty()
        {
            Assert.IsEmpty(LevelGraphValidator.Validate(null));
        }
    }
}
