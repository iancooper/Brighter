﻿using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Paramore.Brighter.Extensions.Tests;

public class TestTransform : IAmAMessageTransformAsync, IAmAMessageTransform
{
    public List<object> WrapInitializerList { get; set; } = new List<object>();
    public List<object> UnwrapInitializerList { get; set; } = new List<object>();
    
    public void Dispose()
    {
        WrapInitializerList.Clear();
    }

    public void InitializeWrapFromAttributeParams(params object[] initializerList)
    {
        WrapInitializerList.AddRange(initializerList);
    }

    public void InitializeUnwrapFromAttributeParams(params object[] initializerList)
    {
       UnwrapInitializerList.AddRange(initializerList); 
    }

    public Message Wrap(Message message)
    {
        return message;
    }

    public Message Unwrap(Message message)
    {
        return message;
    }

    public Task<Message> WrapAsync(Message message, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(message);
        return Task.FromResult(tcs.Task.Result);
    }

    public Task<Message> UnwrapAsync(Message message, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(message);
        return Task.FromResult(tcs.Task.Result);
    }
}
