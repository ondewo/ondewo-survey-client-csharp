using System;
using System.Linq;
using System.Reflection;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Net.Client;
using Xunit;

// `Survey` names both a namespace (Ondewo.Survey) and a message inside it, and the ONE test file
// that spells out concrete types is the only place the ambiguity can bite. Aliasing the message
// once here keeps the rest of the file readable without a global:: prefix on every mention.
using SurveyMessage = Ondewo.Survey.Survey;

namespace Ondewo.Survey.Client.Tests
{
    /// <summary>
    /// The product-specific half of the suite: concrete assertions against the ONDEWO SURVEY API,
    /// spelled out with real message, field, enum and RPC names.
    /// <para>
    /// This is the only test file that has to be rewritten when the setup is replicated to another
    /// ONDEWO product - <see cref="GeneratedStubsTests"/> carries over unchanged.
    /// </para>
    /// </summary>
    public class SurveyStubsTests
    {
        private const string DummyTarget = "http://localhost:50051";

        /// <summary>Every RPC <c>ondewo.survey.Surveys</c> declares, in declaration order.</summary>
        private static readonly string[] SurveysRpcs =
        {
            "CreateSurvey", "GetSurvey", "UpdateSurvey", "DeleteSurvey", "ListSurveys",
            "GetSurveyAnswers", "GetAllSurveyAnswers", "CreateAgentSurvey", "UpdateAgentSurvey",
            "DeleteAgentSurvey",
        };

        /// <summary>Every RPC <c>ondewo.survey.FHIR</c> declares, in declaration order.</summary>
        private static readonly string[] FhirRpcs =
        {
            "CreateFHIRSurvey", "GetFHIRSurveyAnswers", "GetAllFHIRSurveyAnswers",
        };

        [Fact]
        public void SurveyRoundTripsEveryScalarFieldKind()
        {
            var survey = new SurveyMessage
            {
                SurveyId = "survey-42",
                DisplayName = "Patient intake",
                LanguageCode = "de",
                Status = SurveyMessage.Types.AgentStatus.Updated,
            };

            byte[] bytes = survey.ToByteArray();
            SurveyMessage parsed = SurveyMessage.Parser.ParseFrom(bytes);

            Assert.NotEmpty(bytes);
            Assert.Equal(survey, parsed);
            Assert.Equal("survey-42", parsed.SurveyId);
            Assert.Equal("Patient intake", parsed.DisplayName);
            Assert.Equal("de", parsed.LanguageCode);
            Assert.Equal(SurveyMessage.Types.AgentStatus.Updated, parsed.Status);
        }

        [Fact]
        public void SurveyRoundTripsItsNestedSurveyInfoAndItsRepeatedQuestions()
        {
            var survey = new SurveyMessage
            {
                SurveyId = "survey-42",
                SurveyInfo = new SurveyInfo
                {
                    LegalEntity = "ONDEWO GmbH",
                    EmailAddress = "survey@ondewo.com",
                    Topic = "patient satisfaction",
                    Anonymous = true,
                },
            };
            survey.Questions.Add(new Question
            {
                OpenQuestion = new OpenQuestion { QuestionText = "How are you feeling today?" },
            });
            survey.ExcludeSubflows.Add(SubFlow.PhoneNumber);

            SurveyMessage parsed = SurveyMessage.Parser.ParseFrom(survey.ToByteArray());

            Assert.Equal(survey, parsed);
            Assert.Equal("ONDEWO GmbH", parsed.SurveyInfo.LegalEntity);
            Assert.Equal("survey@ondewo.com", parsed.SurveyInfo.EmailAddress);
            Assert.Equal("patient satisfaction", parsed.SurveyInfo.Topic);
            Assert.True(parsed.SurveyInfo.Anonymous);
            Assert.Equal("How are you feeling today?", Assert.Single(parsed.Questions).OpenQuestion.QuestionText);
            Assert.Equal(new[] { SubFlow.PhoneNumber }, parsed.ExcludeSubflows);
        }

        [Fact]
        public void RepeatedFieldRoundTripsThroughAListResponse()
        {
            var response = new ListSurveysResponse { NextPageToken = "page-2" };
            response.Surveys.Add(new SurveyMessage { SurveyId = "survey-1" });
            response.Surveys.Add(new SurveyMessage { SurveyId = "survey-2" });

            ListSurveysResponse parsed = ListSurveysResponse.Parser.ParseFrom(response.ToByteArray());

            Assert.Equal(response, parsed);
            Assert.Equal("page-2", parsed.NextPageToken);
            Assert.Equal(new[] { "survey-1", "survey-2" }, parsed.Surveys.Select(survey => survey.SurveyId));
        }

        [Fact]
        public void UnsetScalarFieldsCarryTheProto3DefaultsAndStayOffTheWire()
        {
            var survey = new SurveyMessage();

            Assert.Equal(string.Empty, survey.SurveyId);
            Assert.Equal(string.Empty, survey.LanguageCode);
            Assert.Equal(SurveyMessage.Types.AgentStatus.ToBeInitialized, survey.Status);
            Assert.Null(survey.SurveyInfo);
            Assert.Empty(survey.Questions);
            Assert.Empty(survey.ToByteArray());
        }

        /// <summary>
        /// The SURVEY API carries no proto3 <c>optional</c> field, but it does declare three real
        /// <c>oneof</c>s - and a oneof is where an explicitly-set <c>false</c> has to stay
        /// distinguishable from a field that was never set at all.
        /// </summary>
        [Fact]
        public void AnswerOneofKeepsAnExplicitFalseDistinguishableFromAnUnsetField()
        {
            var unset = new Answer { SessionId = "session-1" };
            var explicitlyFalse = new Answer { SessionId = "session-1", Anonymous = false };

            Assert.Equal(Answer.IsAnonymousOneofCase.None, unset.IsAnonymousCase);
            Assert.Equal(Answer.IsAnonymousOneofCase.Anonymous, explicitlyFalse.IsAnonymousCase);
            Assert.False(explicitlyFalse.Anonymous);

            Answer parsedUnset = Answer.Parser.ParseFrom(unset.ToByteArray());
            Answer parsedFalse = Answer.Parser.ParseFrom(explicitlyFalse.ToByteArray());

            Assert.False(parsedUnset.HasAnonymous);
            Assert.True(parsedFalse.HasAnonymous);
            Assert.NotEqual(parsedUnset, parsedFalse);

            // Setting the other arm of the oneof evicts this one, rather than adding to it.
            explicitlyFalse.UserInformation = new Answer.Types.UserInfo { UserId = "user-1" };
            Assert.False(explicitlyFalse.HasAnonymous);
            Assert.Equal(Answer.IsAnonymousOneofCase.UserInformation, explicitlyFalse.IsAnonymousCase);
        }

        [Fact]
        public void EnumsStartAtTheirZeroValue()
        {
            Assert.Equal(0, (int)SubFlow.Unspecified);
            Assert.Equal(SubFlow.Unspecified, default(SubFlow));
            Assert.Equal(0, (int)SurveyMessage.Types.AgentStatus.ToBeInitialized);
            Assert.Equal(
                SurveyMessage.Types.AgentStatus.ToBeInitialized,
                default(SurveyMessage.Types.AgentStatus));

            // The C# name is PascalCased; the wire/JSON name is the one the server speaks.
            Assert.Equal("SUBFLOW_UNSPECIFIED", OriginalNameOf(SubFlow.Unspecified));
            Assert.Equal("PHONE_NUMBER", OriginalNameOf(SubFlow.PhoneNumber));
            Assert.Equal("TO_BE_INITIALIZED", OriginalNameOf(SurveyMessage.Types.AgentStatus.ToBeInitialized));
        }

        [Fact]
        public void EnumFieldRoundTripsANonDefaultValue()
        {
            var survey = new SurveyMessage { Status = SurveyMessage.Types.AgentStatus.Outdated };

            SurveyMessage parsed = SurveyMessage.Parser.ParseFrom(survey.ToByteArray());

            Assert.Equal(SurveyMessage.Types.AgentStatus.Outdated, parsed.Status);
            Assert.NotEmpty(survey.ToByteArray());
        }

        [Fact]
        public void SurveysClientBindsToAChannelAndExposesTheDeclaredRpcs()
        {
            using GrpcChannel channel = GrpcChannel.ForAddress(DummyTarget);

            var client = new Surveys.SurveysClient(channel);

            Assert.NotNull(client);
            Assert.Equal("ondewo.survey.Surveys", Surveys.Descriptor.FullName);
            Assert.Equal(SurveysRpcs, Surveys.Descriptor.Methods.Select(method => method.Name));

            string[] clientMethods = typeof(Surveys.SurveysClient)
                .GetMethods()
                .Select(method => method.Name)
                .Distinct()
                .ToArray();

            foreach (string rpc in SurveysRpcs)
            {
                Assert.Contains(rpc, clientMethods);
                Assert.Contains(rpc + "Async", clientMethods);
            }
        }

        /// <summary>
        /// The SURVEY API is the one ONDEWO API that declares a second service, and protoc keeps
        /// its all-caps spelling: the class is <c>FHIR</c> / <c>FHIRClient</c>, not <c>Fhir</c>.
        /// </summary>
        [Fact]
        public void FhirClientIsGeneratedForTheSecondServiceToo()
        {
            using GrpcChannel channel = GrpcChannel.ForAddress(DummyTarget);

            var client = new FHIR.FHIRClient(channel);

            Assert.NotNull(client);
            Assert.Equal("ondewo.survey.FHIR", FHIR.Descriptor.FullName);
            Assert.Equal(FhirRpcs, FHIR.Descriptor.Methods.Select(method => method.Name));

            string[] clientMethods = typeof(FHIR.FHIRClient)
                .GetMethods()
                .Select(method => method.Name)
                .Distinct()
                .ToArray();

            foreach (string rpc in FhirRpcs)
            {
                Assert.Contains(rpc, clientMethods);
                Assert.Contains(rpc + "Async", clientMethods);
            }
        }

        /// <summary>
        /// Unlike csi and vtsi, the SURVEY API vendors no other product's protos: the assembly has
        /// to hold exactly the two ondewo/survey files and nothing else.
        /// </summary>
        [Fact]
        public void TheAssemblyShipsExactlyTheTwoSurveyProtos()
        {
            string[] files = ProductStubs.Assembly.GetTypes()
                .Select(type => type.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static))
                .Where(property => property != null && property.PropertyType == typeof(FileDescriptor))
                .Select(property => ((FileDescriptor)property.GetValue(null)).Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(new[] { "ondewo/survey/fhir.proto", "ondewo/survey/survey.proto" }, files);
        }

        private static string OriginalNameOf<TEnum>(TEnum value)
            where TEnum : struct, Enum
        {
            return typeof(TEnum)
                .GetField(value.ToString())
                .GetCustomAttributes(typeof(OriginalNameAttribute), false)
                .Cast<OriginalNameAttribute>()
                .Single()
                .Name;
        }
    }
}
