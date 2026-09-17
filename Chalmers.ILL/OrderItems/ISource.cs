using System;
using System.Collections.Generic;
using System.Linq;

namespace Chalmers.ILL.OrderItems
{
    public interface ISource
    {
        /// <summary>
        /// Polls the source and creates new orders and updates existing orders in our system from that data.
        /// </summary>
        /// <returns>The result from the polling.</returns>
        SourcePollingResult Poll();

        /// <summary>
        /// The result from the latest polling.
        /// </summary>
        SourcePollingResult Result { get; }
    }
}