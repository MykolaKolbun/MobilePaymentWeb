using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace MobilePaymentWeb.Services.SkidataMobilePay
{
    public class MessageLogger : IClientMessageInspector, IEndpointBehavior
    {
        public void AfterReceiveReply(ref Message reply, object correlationState)
        {
            // Відповідь
            var buffer = reply.CreateBufferedCopy(int.MaxValue);
            var copy = buffer.CreateMessage();
            reply = buffer.CreateMessage();
            System.Diagnostics.Debug.WriteLine(copy.ToString());
        }

        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            // Запит
            var buffer = request.CreateBufferedCopy(int.MaxValue);
            var copy = buffer.CreateMessage();
            request = buffer.CreateMessage();
            System.Diagnostics.Debug.WriteLine(copy.ToString());
            return null;
        }

        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters) { }
        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            clientRuntime.ClientMessageInspectors.Add(this);
        }
        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher) { }
        public void Validate(ServiceEndpoint endpoint) { }
    }
}
