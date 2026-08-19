using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Inkform.LevelGraph.Tests
{
    /// <summary>
    /// Parser tests. These are the project's first automated tests, and the graph file is a good place
    /// to start: it is pure text in / model out, so nothing here needs a scene, a prefab or an asset
    /// fixture — which is exactly why the parser was kept free of AssetDatabase in the first place.
    ///
    /// The round-trip test is the important one. LevelGraph.txt is the source of truth and it lives in
    /// version control, so Serialize(Parse(x)) drifting from x would mean every Apply rewrites lines
    /// nobody edited and the file stops being reviewable.
    /// </summary>
    public class LevelGraphParserTests
    {
        private const string Sample =
            "# a comment\n" +
            "menu   MainMenu\n" +
            "entry  L1_Player\n" +
            "\n" +
            "room   L1_Player  @0,0       \"a laboratory in a cave\"\n" +
            "room   L1_S1      @-260,140  \"a laboratory in a cave\"\n" +
            "room   L1_Boss    @520,0\n" +
            "\n" +
            "link   L1_Player  <-> L1_S1\n" +
            "link   L1_Player  ->  L1_Boss  id:secret_door\n";

        [Test]
        public void Parse_ReadsHeaderRoomsAndLinks()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse(Sample, out List<LevelGraphParser.ParseError> errors);

            Assert.IsEmpty(errors, "clean input should produce no errors");
            Assert.AreEqual("MainMenu", doc.MenuScene);
            Assert.AreEqual("L1_Player", doc.EntryRoom);
            Assert.AreEqual(3, doc.Rooms.Count);
            Assert.AreEqual(2, doc.Links.Count);
        }

        [Test]
        public void Parse_ReadsPositionAndDisplayName()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse(Sample);

            RoomEntry s1 = doc.FindRoom("L1_S1");
            Assert.IsNotNull(s1);
            Assert.AreEqual(new Vector2(-260f, 140f), s1.Position);
            Assert.AreEqual("a laboratory in a cave", s1.DisplayName);
        }

        [Test]
        public void Parse_RoomWithoutDisplayName_LeavesItEmpty()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse(Sample);

            RoomEntry boss = doc.FindRoom("L1_Boss");
            Assert.IsNotNull(boss);
            Assert.AreEqual(string.Empty, boss.DisplayName);
            Assert.AreEqual(new Vector2(520f, 0f), boss.Position);
        }

        [Test]
        public void Parse_ArrowFormDecidesDirectionality()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse(Sample);

            Assert.IsTrue(doc.Links[0].Bidirectional, "<-> should parse as bidirectional");
            Assert.IsFalse(doc.Links[1].Bidirectional, "-> should parse as one-way");
            Assert.AreEqual("secret_door", doc.Links[1].ExitId);
        }

        [Test]
        public void EnumerateDirected_ExpandsBidirectionalIntoBothDirections()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse(Sample);
            var directed = new List<DirectedLink>(doc.EnumerateDirected());

            // 1 bidirectional (2 edges) + 1 one-way (1 edge)
            Assert.AreEqual(3, directed.Count);

            Assert.AreEqual("L1_Player", directed[0].From);
            Assert.AreEqual("L1_S1", directed[0].To);
            Assert.AreEqual("L1_S1", directed[0].ExitId, "default exit id is the target room name");

            Assert.AreEqual("L1_S1", directed[1].From);
            Assert.AreEqual("L1_Player", directed[1].To);
            Assert.AreEqual("L1_Player", directed[1].ExitId, "the reverse edge takes its own default id");

            Assert.AreEqual("secret_door", directed[2].ExitId, "an explicit id survives expansion");
        }

        [Test]
        public void Serialize_ThenParse_RoundTripsWithoutDrift()
        {
            LevelGraphDocument first = LevelGraphParser.Parse(Sample);
            string text = LevelGraphParser.Serialize(first);
            LevelGraphDocument second = LevelGraphParser.Parse(text, out List<LevelGraphParser.ParseError> errors);

            Assert.IsEmpty(errors);
            AssertSameDocument(first, second);

            // And the text itself must be a fixed point, or every Apply churns the diff.
            Assert.AreEqual(text, LevelGraphParser.Serialize(second));
        }

        [Test]
        public void Serialize_UsesLfOnly()
        {
            string text = LevelGraphParser.Serialize(LevelGraphParser.Parse(Sample));

            Assert.IsFalse(text.Contains("\r"),
                "the repo normalises text to LF; emitting CRLF would make every Apply look like a full-file rewrite");
        }

        [Test]
        public void Parse_CommentInsideQuotes_IsNotTreatedAsAComment()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse("room A @0,0 \"tunnel #3\"\n");

            Assert.AreEqual("tunnel #3", doc.FindRoom("A").DisplayName);
        }

        [Test]
        public void Parse_IdOnBidirectionalLink_IsRejected()
        {
            LevelGraphParser.Parse("link A <-> B id:oops\n", out List<LevelGraphParser.ParseError> errors);

            Assert.AreEqual(1, errors.Count, "a custom id cannot say which direction it means");
        }

        [Test]
        public void Parse_DuplicateRoom_IsReportedAndSkipped()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse("room A @0,0\nroom A @1,1\n", out List<LevelGraphParser.ParseError> errors);

            Assert.AreEqual(1, errors.Count);
            Assert.AreEqual(1, doc.Rooms.Count);
        }

        // A designer with a typo on one line must still see every other room in the window, so the
        // parser reports and continues rather than throwing.
        [Test]
        public void Parse_MalformedInput_DoesNotThrowAndKeepsTheGoodLines()
        {
            const string broken =
                "menu\n" +                       // missing argument
                "room\n" +                       // missing name
                "room  Good  @xx,yy\n" +         // unparseable position
                "link  A  ~~  B\n" +             // not an arrow
                "banana\n" +                     // unknown keyword
                "room  AlsoGood @10,20\n";

            LevelGraphDocument doc = null;
            List<LevelGraphParser.ParseError> errors = null;
            Assert.DoesNotThrow(() => doc = LevelGraphParser.Parse(broken, out errors));

            Assert.GreaterOrEqual(errors.Count, 5);
            Assert.IsTrue(doc.HasRoom("Good"), "a bad position must not lose the room");
            Assert.IsTrue(doc.HasRoom("AlsoGood"), "parsing must continue past a bad line");
        }

        [Test]
        public void Parse_EmptyOrNullInput_YieldsAnEmptyDocument()
        {
            Assert.AreEqual(0, LevelGraphParser.Parse(null).Rooms.Count);
            Assert.AreEqual(0, LevelGraphParser.Parse("").Rooms.Count);
            Assert.AreEqual(0, LevelGraphParser.Parse("\n\n# just a comment\n").Rooms.Count);
        }

        [Test]
        public void Parse_AcceptsCrLfInput()
        {
            LevelGraphDocument doc = LevelGraphParser.Parse("menu M\r\nentry A\r\nroom A @0,0\r\n", out var errors);

            Assert.IsEmpty(errors);
            Assert.AreEqual("A", doc.EntryRoom);
            Assert.IsTrue(doc.HasRoom("A"));
        }

        private static void AssertSameDocument(LevelGraphDocument a, LevelGraphDocument b)
        {
            Assert.AreEqual(a.MenuScene, b.MenuScene);
            Assert.AreEqual(a.EntryRoom, b.EntryRoom);
            Assert.AreEqual(a.Rooms.Count, b.Rooms.Count);
            Assert.AreEqual(a.Links.Count, b.Links.Count);

            for (int i = 0; i < a.Rooms.Count; i++)
            {
                Assert.AreEqual(a.Rooms[i].Name, b.Rooms[i].Name, $"room {i} name");
                Assert.AreEqual(a.Rooms[i].DisplayName, b.Rooms[i].DisplayName, $"room {i} display name");
                Assert.AreEqual(a.Rooms[i].Position, b.Rooms[i].Position, $"room {i} position");
            }

            for (int i = 0; i < a.Links.Count; i++)
            {
                Assert.AreEqual(a.Links[i].From, b.Links[i].From, $"link {i} from");
                Assert.AreEqual(a.Links[i].To, b.Links[i].To, $"link {i} to");
                Assert.AreEqual(a.Links[i].Bidirectional, b.Links[i].Bidirectional, $"link {i} arrow");
                Assert.AreEqual(a.Links[i].ExitId, b.Links[i].ExitId, $"link {i} id");
            }
        }
    }
}
