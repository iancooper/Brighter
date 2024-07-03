﻿using System;
using System.Collections.Generic;
using System.Transactions;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Paramore.Brighter.Core.Tests.CommandProcessors.TestDoubles;
using Paramore.Brighter.Core.Tests.TestHelpers;
using Paramore.Brighter.Observability;
using Polly;
using Polly.Registry;
using Xunit;

namespace Paramore.Brighter.Core.Tests.CommandProcessors.Call
{
    [Collection("CommandProcessor")]
    public class CommandProcessorNoInMapperTests : IDisposable
    {
        private readonly CommandProcessor _commandProcessor;
        private readonly MyRequest _myRequest = new MyRequest();

        public CommandProcessorNoInMapperTests()
        {
             _myRequest.RequestValue = "Hello World";

            var messageMapperRegistry = new MessageMapperRegistry(new SimpleMessageMapperFactory((type) =>
            {
                if (type == typeof(MyRequestMessageMapper))
                    return new MyRequestMessageMapper();

                throw new ConfigurationException($"No mapper found for {type.Name}");
            }), null);
            
            messageMapperRegistry.Register<MyRequest, MyRequestMessageMapper>();

            var subscriberRegistry = new SubscriberRegistry();
            subscriberRegistry.Register<MyResponse, MyResponseHandler>();
            var handlerFactory = new SimpleHandlerFactorySync(_ => new MyResponseHandler());

            var retryPolicy = Policy
                .Handle<Exception>()
                .Retry();

            var circuitBreakerPolicy = Policy
                .Handle<Exception>()
                .CircuitBreaker(1, TimeSpan.FromMilliseconds(1));

            var replySubscriptions = new List<Subscription>();

            const string topic = "MyRequest";
            var timeProvider = new FakeTimeProvider();
            var producerRegistry = new ProducerRegistry(new Dictionary<string, IAmAMessageProducer>
            {
                { topic, new InMemoryProducer(new InternalBus(), timeProvider)
                {
                    Publication = {Topic = new RoutingKey(topic), RequestType = typeof(MyRequest)}
                } },
            });

            var policyRegistry = new PolicyRegistry
            {
                { CommandProcessor.RETRYPOLICY, retryPolicy },
                { CommandProcessor.CIRCUITBREAKER, circuitBreakerPolicy }
            };

            var tracer = new BrighterTracer();
            
            IAmAnExternalBusService bus = new ExternalBusService<Message, CommittableTransaction>(
                producerRegistry, 
                policyRegistry, 
                messageMapperRegistry,
                new EmptyMessageTransformerFactory(),
                new EmptyMessageTransformerFactoryAsync(),
                tracer,
                new InMemoryOutbox( timeProvider) {Tracer = tracer}
                );
        
            CommandProcessor.ClearServiceBus();
            _commandProcessor = new CommandProcessor(
                subscriberRegistry,
                handlerFactory,
                new InMemoryRequestContextFactory(), 
                policyRegistry,
                bus,
                replySubscriptions:replySubscriptions,
                responseChannelFactory: new InMemoryChannelFactory(new InternalBus(), TimeProvider.System)
            );
            
            PipelineBuilder<MyResponse>.ClearPipelineCache();
        } 
        
        [Fact]
        public void When_Calling_A_Server_Via_The_Command_Processor_With_No_Out_Mapper()
        {
            var exception = Catch.Exception(() => _commandProcessor.Call<MyRequest, MyResponse>(_myRequest, new RequestContext(), 500));
            
            //should throw an exception as we require a mapper for the outgoing request 
            exception.Should().BeOfType<ArgumentOutOfRangeException>();
        }

        public void Dispose()
        {
            CommandProcessor.ClearServiceBus();
        }
    }
}
