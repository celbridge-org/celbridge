namespace Celbridge.BaseLibrary.Messaging
{
    /// <summary>
    /// Send and register to listen for messages.
    /// </summary>
    public interface IMessengerService
    {
        /// <summary>
        /// Register a handler method, associated with a recipient object, for a message type. 
        /// </summary>
        void Register<TMessage>(object recipient, MessageHandler<object, TMessage> handler) where TMessage : class;

        /// <summary>
        /// Unregister all registed handler methods.
        /// </summary>
        void UnregisterAll(object recipient);

        /// <summary>
        /// Send a message to all registered handlers.
        /// No additional information is associated with the message.
        /// </summary>
        TMessage Send<TMessage>() where TMessage : class, new();

        /// <summary>
        /// Send a message to all registered handlers.
        /// The message object can be used to convey information to the handlers.
        /// </summary>
        TMessage Send<TMessage>(TMessage message) where TMessage : class;
    }
}
