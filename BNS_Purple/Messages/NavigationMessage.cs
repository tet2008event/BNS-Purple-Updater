using CommunityToolkit.Mvvm.Messaging.Messages;

namespace BNS_Purple.Messages
{
    public  class NavigationMessage : ValueChangedMessage<string>
    {
        public NavigationMessage(string message) : base(message) { }
    }
}
