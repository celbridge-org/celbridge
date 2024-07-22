namespace Celbridge.Messaging;

/// <summary>
/// Delegate signature for a message handler.
/// This is a straightforward wrapper for CommunityToolkit.Mvvm.Messaging.MessageHandler.
/// </summary>
public delegate void MessageHandler<in TRecipient, in TMessage>(TRecipient recipient, TMessage message)
    where TRecipient : class
    where TMessage : class;