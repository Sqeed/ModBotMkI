namespace ModBot
{
    public enum PermissionLevel : byte
    {
        Newb = 0,
        Member, 
        ChannelModerator, //Manage Messages (Channel)
        ChannelAdmin, //Manage Permissions (Channel)
        ServerModerator, //Manage Messages, Kick, Ban (Server)
        ServerAdmin, //Manage Roles (Server)
        ServerOwner, //Owner (Server)
        BotOwner, //Bot Owner (Global)
    }
}
