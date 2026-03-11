using Nadosh.Core.Configuration;

namespace Nadosh.Core.Interfaces;

public interface IQueuePolicyProvider
{
    ResolvedQueuePolicy GetPolicy<T>();

    ResolvedQueuePolicy GetPolicy(string queueName);
}