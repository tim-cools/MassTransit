// Copyright 2007-2016 Chris Patterson, Dru Sellers, Travis Smith, et. al.
//  
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use
// this file except in compliance with the License. You may obtain a copy of the 
// License at 
// 
//     http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software distributed
// under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
// CONDITIONS OF ANY KIND, either express or implied. See the License for the 
// specific language governing permissions and limitations under the License.
namespace MassTransit.Containers.Tests
{
    using System;
    using System.Threading.Tasks;
    using Autofac;
    using NUnit.Framework;
    using Scenarios;
    using Shouldly;
    using TestFramework;
    using Testing;


    [TestFixture]
    public class AutofacContainer_Setup :
        AsyncTestFixture
    {
        [Test]
        public async Task Should_work_with_lifecycle_managed_bus()
        {
            var bus = _container.Resolve<IBusControl>();

            BusHandle busHandle = await bus.StartAsync();
            try
            {
                const string name = "Joe";

                await bus.Publish(new SimpleMessageClass(name));

                SimpleConsumer lastConsumer = await SimpleConsumer.LastConsumer;
                lastConsumer.ShouldNotBe(null);

                SimpleMessageInterface last = await lastConsumer.Last;
                last.Name
                    .ShouldBe(name);

                bool wasDisposed = await lastConsumer.Dependency.WasDisposed;
                wasDisposed
                    .ShouldBe(true); //Dependency was not disposed");

                lastConsumer.Dependency.SomethingDone
                    .ShouldBe(true); //Dependency was disposed before consumer executed");

                var called = await _consumeObserver.VerifyCalled;
                called
                    .ShouldBe(true); //"ILifetimeScope in found in context payload"
            }
            finally
            {
                await busHandle.StopAsync();
            }
        }

        public AutofacContainer_Setup()
            : base(new InMemoryTestHarness())
        {
        }

        IContainer _container;
        ValidateMessageConsumerHasLifestyle _consumeObserver;

        [OneTimeSetUp]
        public void Setup()
        {
            _consumeObserver = new ValidateMessageConsumerHasLifestyle();

            var builder = new ContainerBuilder();

            builder.RegisterModule(new BusModule(_consumeObserver));
            builder.RegisterModule<ConsumerModule>();

            _container = builder.Build();
        }


        class ConsumerModule :
            Module
        {
            protected override void Load(ContainerBuilder builder)
            {
                builder.RegisterType<SimpleConsumer>();
                builder.RegisterType<SimpleConsumerDependency>()
                    .As<ISimpleConsumerDependency>()
                    .InstancePerLifetimeScope();
                builder.RegisterType<AnotherMessageConsumerImpl>()
                    .As<AnotherMessageConsumer>();
            }
        }


        class BusModule :
            Module
        {
            ValidateMessageConsumerHasLifestyle _consumeObserver;

            public BusModule(ValidateMessageConsumerHasLifestyle consumeObserver)
            {
                _consumeObserver = consumeObserver;
            }

            protected override void Load(ContainerBuilder builder)
            {
                builder.Register(context =>
                    {
                        var bus = Bus.Factory.CreateUsingInMemory(x => x.ReceiveEndpoint("input_queue", e => e.LoadFrom(context)));
                        bus.ConnectConsumeObserver(_consumeObserver);
                        return bus;
                    })
                    .As<IBus>()
                    .As<IBusControl>()
                    .OnRelease(control => control.Stop())
                    .SingleInstance();
            }
        }


        [OneTimeTearDown]
        public void Teardown()
        {
            _container.Dispose();
        }
    }


    public class ValidateMessageConsumerHasLifestyle : IConsumeObserver
    {
        static readonly TaskCompletionSource<bool> _verifyCalled = new TaskCompletionSource<bool>();

        public Task<bool> VerifyCalled => _verifyCalled.Task;

        public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception) where T : class => Task.CompletedTask;

        public Task PostConsume<T>(ConsumeContext<T> context) where T : class
        {
            var tryGetPayload = context.TryGetPayload<ILifetimeScope>(out var lifetimeScope);
            var simpleConsumerDependency = lifetimeScope?.Resolve<ISimpleConsumerDependency>();

            _verifyCalled.TrySetResult(tryGetPayload && simpleConsumerDependency.SomethingDone);

            return Task.CompletedTask;
        }

        public Task PreConsume<T>(ConsumeContext<T> context) where T : class => Task.CompletedTask;
    }
}