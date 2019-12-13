

namespace Acrelec.Mockingbird.Payment
{
    public enum CommunicatorMethods : int
    {
        /// <summary>
        /// The name of the init method that is send/received in a pipe message
        /// </summary>
        Init = 0,

        /// <summary>
        /// The name of the test method that is send/received in a pipe message
        /// </summary>
        Test = 1,

        /// <summary>
        /// The name of the start receiving money method that is send/received in a pipe message
        /// </summary>
        Pay = 2,

        /// <summary>
        /// The name of the progress message method that is send/received in a pipe message
        /// </summary>
        ProgressMessage = 3
    }
}
