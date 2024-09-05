﻿
using Microsoft.Copilot.BotBuilder;
using Microsoft.Copilot.Protocols.Primitives;
using Microsoft.Copilot.Teams.Primitives;
using Microsoft.Teams.AI.Tests.TestUtils;
using Microsoft.Teams.AI.Exceptions;
using Microsoft.Copilot.Protocols.Adapter;

namespace Microsoft.Teams.AI.Tests.Application.Authentication.MessageExtensions
{
    internal sealed class MockedMessageExtensionsAuthentication : MessageExtensionsAuthenticationBase
    {
        private TokenResponse? _tokenExchangeResponse;
        private TokenResponse? _signInResponse;

        public MockedMessageExtensionsAuthentication(TokenResponse? tokenExchangeResponse = null, TokenResponse? signInResponse = null)
        {
            _tokenExchangeResponse = tokenExchangeResponse;
            _signInResponse = signInResponse;
        }

        public override Task<string> GetSignInLink(ITurnContext context)
        {
            return Task.FromResult("mocked link");
        }

        public override Task<TokenResponse> HandleUserSignIn(ITurnContext context, string magicCode)
        {
            if (_signInResponse == null)
            {
                throw new AuthException("HandlerUserSignIn failed");
            }
            return Task.FromResult(_signInResponse);
        }

        public override Task<TokenResponse> HandleSsoTokenExchange(ITurnContext context)
        {
            if (_tokenExchangeResponse == null)
            {
                throw new AuthException("HandleSsoTokenExchange failed");
            }
            return Task.FromResult(_tokenExchangeResponse);
        }

        public override bool IsSsoSignIn(ITurnContext context)
        {
            return context.Activity.Name == MessageExtensionsInvokeNames.QUERY_INVOKE_NAME;
        }
    }

    internal sealed class TokenExchangeRequest
    {
        public Authentication? authentication { get; set; }
    }

    internal sealed class Authentication
    {
        public string? token { get; set; }
    }

    public class MessageExtensionsAuthenticationBaseTests
    {
        [Fact]
        public async void Test_Authenticate_TokenExchange_Success()
        {
            // arrange
            var meAuth = new MockedMessageExtensionsAuthentication(tokenExchangeResponse: new TokenResponse(token: "test token"));
            var context = MockTurnContext(MessageExtensionsInvokeNames.QUERY_INVOKE_NAME, new SimpleAdapter());
            context.Activity.Value = new TokenExchangeRequest()
            {
                authentication = new Authentication()
                {
                    token = "sso token"
                }
            };

            // act
            var response = await meAuth.AuthenticateAsync(context);

            // assert
            Assert.Equal("test token", response);
        }

        [Fact]
        public async void Test_Authenticate_TokenExchange_Fail()
        {
            // arrange
            var meAuth = new MockedMessageExtensionsAuthentication();
            IActivity[]? activities = null;
            void CaptureSend(IActivity[] arg)
            {
                activities = arg;
            }
            var adapter = new SimpleAdapter(CaptureSend);
            var context = MockTurnContext(MessageExtensionsInvokeNames.QUERY_INVOKE_NAME, adapter);
            context.Activity.Value = new TokenExchangeRequest()
            {
                authentication = new Authentication()
                {
                    token = "sso token"
                }
            };

            // act
            var token = await meAuth.AuthenticateAsync(context);

            // assert
            Assert.Equal(null, token);
            Assert.NotNull(activities);
            var sentActivity = activities.FirstOrDefault();
            Assert.NotNull(sentActivity);
            Assert.Equal(ActivityTypes.InvokeResponse, sentActivity.Type);
            Assert.Equal(412, ((InvokeResponse)sentActivity.Value).Status);
        }

        [Fact]
        public async void Test_Authenticate_UserSignIn_Success()
        {
            // arrange
            var meAuth = new MockedMessageExtensionsAuthentication(signInResponse: new TokenResponse(token: "test token"));
            var context = MockTurnContext(MessageExtensionsInvokeNames.QUERY_INVOKE_NAME, new SimpleAdapter());
            context.Activity.Value = new MessagingExtensionQuery(state: "123456");

            // act
            var token = await meAuth.AuthenticateAsync(context);

            // assert
            Assert.Equal("test token", token);
        }

        [Fact]
        public async void Test_Authenticate_TriggerSignin()
        {
            // arrange
            var meAuth = new MockedMessageExtensionsAuthentication();
            IActivity[]? activities = null;
            void CaptureSend(IActivity[] arg)
            {
                activities = arg;
            }
            var adapter = new SimpleAdapter(CaptureSend);
            var context = MockTurnContext(MessageExtensionsInvokeNames.QUERY_INVOKE_NAME, adapter);
            context.Activity.Value = new MessagingExtensionQuery();

            // act
            var token = await meAuth.AuthenticateAsync(context);

            // assert
            Assert.Equal(null, token);
            Assert.NotNull(activities);
            var sentActivity = activities.FirstOrDefault();
            Assert.NotNull(sentActivity);
            Assert.Equal(ActivityTypes.InvokeResponse, sentActivity.Type);
            var invokeResponse = sentActivity.Value as InvokeResponse;
            Assert.NotNull(invokeResponse);
            var meResponse = invokeResponse.Body as MessagingExtensionResponse;
            Assert.NotNull(meResponse);
            Assert.True(meResponse.ComposeExtension.SuggestedActions.Actions.Count > 0);
            var suggestedAction = meResponse.ComposeExtension.SuggestedActions.Actions.FirstOrDefault();
            Assert.NotNull(suggestedAction);
            Assert.Equal(ActionTypes.OpenUrl, suggestedAction.Type);
            Assert.Equal("mocked link", suggestedAction.Value);
        }

        private static TurnContext MockTurnContext(string name, SimpleAdapter adapter, string type = ActivityTypes.Invoke)
        {
            return new TurnContext(adapter, new Activity()
            {
                Type = type,
                Recipient = new() { Id = "recipientId" },
                Conversation = new() { Id = "conversationId" },
                From = new() { Id = "fromId" },
                ChannelId = "channelId",
                Name = name
            });
        }
    }
}
