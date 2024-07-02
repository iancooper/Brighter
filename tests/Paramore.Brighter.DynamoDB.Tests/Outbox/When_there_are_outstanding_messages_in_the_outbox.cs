﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Paramore.Brighter.Outbox.DynamoDB;
using Xunit;

namespace Paramore.Brighter.DynamoDB.Tests.Outbox;

[Trait("Category", "DynamoDB")]
public class DynamoDbOutboxOutstandingMessageTests : DynamoDBOutboxBaseTest
{
    private readonly Message _message;
    private readonly DynamoDbOutbox _dynamoDbOutbox;

    public DynamoDbOutboxOutstandingMessageTests()
    {
        _message = CreateMessage("test_topic");
        _dynamoDbOutbox = new DynamoDbOutbox(Client, new DynamoDbConfiguration(OutboxTableName));
    }

    [Fact]
    public async Task When_there_are_outstanding_messages_in_the_outbox_async()
    {
        var context = new RequestContext();
        await _dynamoDbOutbox.AddAsync(_message, context);

        await Task.Delay(1000);

        var args = new Dictionary<string, object> {{"Topic", "test_topic"}};

        var messages = await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 100, 1, args);

        //Other tests may leave messages, so make sure that we grab ours
        var message = messages.Single(m => m.Id == _message.Id);
        message.Should().NotBeNull();
        message.Body.Value.Should().Be(_message.Body.Value);
    }

    [Fact]
    public async Task When_there_are_outstanding_messages_in_the_outbox()
    {
        var context = new RequestContext();
        _dynamoDbOutbox.Add(_message, context);

        await Task.Delay(1000);

        var args = new Dictionary<string, object> {{"Topic", "test_topic"}};

        var messages =_dynamoDbOutbox.OutstandingMessages(0, context, 100, 1, args);

        //Other tests may leave messages, so make sure that we grab ours
        var message = messages.Single(m => m.Id == _message.Id);
        message.Should().NotBeNull();
        message.Body.Value.Should().Be(_message.Body.Value);
    }

    [Fact]
    public async Task When_there_are_outstanding_messages_for_multiple_topics_async()
    {
        var messages = new List<Message>();
        var context = new RequestContext();
        messages.Add(CreateMessage("one_topic"));
        messages.Add(CreateMessage("another_topic"));

        foreach (var message in messages)
        {
            await _dynamoDbOutbox.AddAsync(message, context);
        }

        await Task.Delay(1000);

        var outstandingMessages = await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 100, 1);

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
            outstandingMessage.Header.Topic.Should().Be(message.Header.Topic);
        }
    }

    [Fact]
    public async Task When_there_are_outstanding_messages_for_multiple_topics()
    {
        var messages = new List<Message>();
        var context = new RequestContext();
        messages.Add(CreateMessage("one_topic"));
        messages.Add(CreateMessage("another_topic"));

        foreach (var message in messages)
        {
            _dynamoDbOutbox.Add(message, context);
        }

        await Task.Delay(1000);

        var outstandingMessages = _dynamoDbOutbox.OutstandingMessages(0, context, 100, 1);

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
            outstandingMessage.Header.Topic.Should().Be(message.Header.Topic);
        }
    }

    [Fact]
    public async Task When_there_are_multiple_pages_of_outstanding_messages_for_a_topic_async()
    {
        var context = new RequestContext();
        var messages = new List<Message>();
        // Create enough messages to guarantee they will be split across multiple shards
        for (var i = 0; i < 10; i++)
        {
            messages.Add(CreateMessage("test_topic"));
        }

        foreach (var message in messages)
        {
            await _dynamoDbOutbox.AddAsync(message, context);
        }

        await Task.Delay(1000);

        var args = new Dictionary<string, object> { { "Topic", "test_topic" } };

        // Get the first page
        var outstandingMessages = (await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 5, 1, args)).ToList();
        outstandingMessages.Count.Should().Be(5);
        // Get the remainder
        outstandingMessages.AddRange(await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 100, 2, args));

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
        }
    }

    [Fact]
    public async Task When_there_are_multiple_pages_of_outstanding_messages_for_a_topic()
    {
        var context = new RequestContext();
        var messages = new List<Message>();
        // Create enough messages to guarantee they will be split across multiple shards
        for (var i = 0; i < 10; i++)
        {
            messages.Add(CreateMessage("test_topic"));
        }

        foreach (var message in messages)
        {
            _dynamoDbOutbox.Add(message, context);
        }

        await Task.Delay(1000);

        var args = new Dictionary<string, object> { { "Topic", "test_topic" } };

        // Get the first page
        var outstandingMessages = _dynamoDbOutbox.OutstandingMessages(0, context, 5, 1, args).ToList();
        outstandingMessages.Count.Should().Be(5);
        // Get the remainder
        outstandingMessages.AddRange(_dynamoDbOutbox.OutstandingMessages(0, context, 100, 2, args));

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
        }
    }

    [Fact]
    public async Task When_there_are_multiple_pages_of_outstanding_messages_for_all_topics_async()
    {
        var context = new RequestContext();
        var messages = new List<Message>();

        // Create enough messages to guarantee they will be split across multiple shards
        // for all topics
        var topics = new[] { "one_topic", "another_topic" };
        foreach (var topic in topics)
        {
            for (var i = 0; i < 10; i++)
            {
                messages.Add(CreateMessage(topic));
            }
        }

        foreach (var message in messages)
        {
            await _dynamoDbOutbox.AddAsync(message, context);
        }

        await Task.Delay(1000);

        // Get the messages over 4 pages
        var outstandingMessages = new List<Message>();
        for (var i = 1; i < 5; i++)
        {
            outstandingMessages.AddRange(await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 5, i));
        }
        // Do a last page in case other tests have added more messages
        outstandingMessages.AddRange(await _dynamoDbOutbox.OutstandingMessagesAsync(0, context, 100, 5));

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
            outstandingMessage.Header.Topic.Should().Be(message.Header.Topic);
        }
    }

    [Fact]
    public async Task When_there_are_multiple_pages_of_outstanding_messages_for_all_topics()
    {
        var context = new RequestContext();
        var messages = new List<Message>();

        // Create enough messages to guarantee they will be split across multiple shards
        // for all topics
        var topics = new[] { "one_topic", "another_topic" };
        foreach (var topic in topics)
        {
            for (var i = 0; i < 10; i++)
            {
                messages.Add(CreateMessage(topic));
            }
        }

        foreach (var message in messages)
        {
            _dynamoDbOutbox.Add(message, context);
        }

        await Task.Delay(1000);

        // Get the messages over 4 pages
        var outstandingMessages = new List<Message>();
        for (var i = 1; i < 5; i++)
        {
            outstandingMessages.AddRange(_dynamoDbOutbox.OutstandingMessages(0, context, 5, i));
        }
        // Do a last page in case other tests have added more messages
        outstandingMessages.AddRange(_dynamoDbOutbox.OutstandingMessages(0, context, 100, 5));

        //Other tests may leave messages, so make sure that we grab ours
        foreach (var message in messages)
        {
            var outstandingMessage = outstandingMessages.Single(m => m.Id == message.Id);
            outstandingMessage.Should().NotBeNull();
            outstandingMessage.Body.Value.Should().Be(message.Body.Value);
            outstandingMessage.Header.Topic.Should().Be(message.Header.Topic);
        }
    }

    private Message CreateMessage(string topic)
    {
        return new Message(
            new MessageHeader(Guid.NewGuid().ToString(), topic, MessageType.MT_DOCUMENT),
            new MessageBody("message body")
        );
    }
}
