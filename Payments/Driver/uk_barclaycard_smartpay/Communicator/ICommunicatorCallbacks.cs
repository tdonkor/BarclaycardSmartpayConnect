namespace Acrelec.Mockingbird.Payment
{
    public interface ICommunicatorCallbacks
    {
        void InitRequest(object parameters);

        void TestRequest(object parameters);

        void PayRequest(object parameters);
    }
}
