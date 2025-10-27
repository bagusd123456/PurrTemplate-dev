namespace NyxMachina.Shared.EventFramework.Core.Payloads
{
    /// <summary>
    /// Represents the data payload for an event.
    /// This is a marker interface; Implementing classes should define specific payload data.
    /// </summary>
    public interface IPayload
    {
        
    }

    /// <summary>
    /// Provides a convenient extension method to publish any payload.
    /// </summary>
    public static class PayloadExtensions
    {
        /// <summary>
        /// Publishes the payload to the main EventMessenger instance.
        /// </summary>
        /// <param name="payload">The payload to publish.</param>
        public static void Publish(this IPayload payload)
        {
            EVENT.Publish(payload);
        }
    }
}