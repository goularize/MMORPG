using Shared.Constants;

namespace Shared.Tests
{
    public class EntityIdsTests
    {
        [Theory]
        [InlineData(EntityIdKind.Npc, 1)]
        [InlineData(EntityIdKind.Resource, 12345)]
        [InlineData(EntityIdKind.Satchel, EntityIds.MaxSequence)]
        [InlineData(EntityIdKind.Player, 77)]
        public void Compose_RoundTripsKindAndSequence_AndStaysPositive(EntityIdKind kind, int sequence)
        {
            int id = EntityIds.Compose(kind, sequence);

            Assert.True(id > 0);
            Assert.Equal(kind, EntityIds.GetKind(id));
            Assert.Equal(sequence, EntityIds.GetSequence(id));
        }

        [Fact]
        public void SameSequenceInDifferentKinds_NeverCollides()
        {
            var ids = new HashSet<int>();
            foreach (EntityIdKind kind in Enum.GetValues<EntityIdKind>())
            {
                Assert.True(ids.Add(EntityIds.Compose(kind, 100000)));
            }
        }

        [Fact]
        public void PlayerId_IsTheDatabaseIdUnchanged()
        {
            Assert.Equal(42, EntityIds.Compose(EntityIdKind.Player, 42));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(EntityIds.MaxSequence + 1)]
        public void Compose_OutOfRangeSequence_Throws(int sequence)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => EntityIds.Compose(EntityIdKind.Npc, sequence));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(EntityIds.MaxSequence, true)]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        [InlineData(EntityIds.MaxSequence + 1, false)]
        public void IsValidPlayerId_OnlyAcceptsIdsInsideThePlayerRange(int characterId, bool expected)
        {
            Assert.Equal(expected, EntityIds.IsValidPlayerId(characterId));
        }

        [Fact]
        public void Describe_ShowsKindAndSequence()
        {
            Assert.Equal("Npc:7", EntityIds.Describe(EntityIds.Compose(EntityIdKind.Npc, 7)));
        }
    }
}
