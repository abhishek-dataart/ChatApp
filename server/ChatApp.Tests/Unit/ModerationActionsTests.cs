using ChatApp.Domain.Services.Rooms;
using FluentAssertions;
using Xunit;

namespace ChatApp.Tests.Unit;

public class ModerationActionsTests
{
    [Fact]
    public void Action_constants_are_stable_strings()
    {
        // These are persisted to moderation_audit rows — renaming breaks history.
        ModerationActions.Ban.Should().Be("ban");
        ModerationActions.Unban.Should().Be("unban");
        ModerationActions.Kick.Should().Be("kick");
        ModerationActions.RoleChange.Should().Be("role_change");
        ModerationActions.CapacityChange.Should().Be("capacity_change");
        ModerationActions.RoomDelete.Should().Be("room_delete");
    }

    [Fact]
    public void All_action_values_are_unique()
    {
        string[] all =
        {
            ModerationActions.Ban,
            ModerationActions.Unban,
            ModerationActions.Kick,
            ModerationActions.RoleChange,
            ModerationActions.CapacityChange,
            ModerationActions.RoomDelete,
        };
        all.Should().OnlyHaveUniqueItems();
    }
}
