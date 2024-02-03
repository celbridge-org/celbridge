namespace Celbridge.BaseLibrary.Messaging
{
    /// <summary>
    /// Send and register to listen for messages.
    /// </summary>
    public interface IMessengerService
    {
        void Register<TMessage>(object recipient, MessageHandler<object, TMessage> handler) where TMessage : class;

        void UnregisterAll(object recipient);

        TMessage Send<TMessage>()
            where TMessage : class, new();

        TMessage Send<TMessage>(TMessage message)
            where TMessage : class;
    }
}
