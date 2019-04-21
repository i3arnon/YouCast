using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using YouCast.Helpers;

namespace YouCast.Tests.Helpers
{
    public class NetShHelperFirewallTests : IDisposable
    {
        private const string TestRuleName = "NetShHelperTests";
        private const int TestPort = 2345;
        private const int TestPort2 = 23456;

        [SkippableFact]
        public void ShouldBeAbleToCreateFirewallRule()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();

            var result = sut.CreateFirewallRule(TestRuleName, TestPort);

            result.Should().BeTrue();
        }

        [SkippableFact]
        public void ShouldBeAbleToGetFirewallRule()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();
            var createResult = sut.CreateFirewallRule(TestRuleName, TestPort);
            createResult.Should().BeTrue();

            var result = sut.GetFirewallRule(TestRuleName);

            result.Should().NotBeNull();
            result.Rules.First().RuleName.Should().Be(TestRuleName);
            result.Rules.First().LocalPort.Should().Be(TestPort);
        }

        [SkippableFact]
        public void ShouldBeAbleToDeleteFirewallRule()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();
            var createResult = sut.CreateFirewallRule(TestRuleName, TestPort);
            createResult.Should().BeTrue();

            var result = sut.DeleteFirewallRule(TestRuleName);

            result.Should().BeTrue();
            var getResult = sut.GetFirewallRule(TestRuleName);
            getResult.Should().NotBeNull();
            getResult.Rules.Should().BeEmpty();
        }

        [SkippableFact]
        public void ShouldBeAbleToUpdateFirewallRule()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();
            var createResult = sut.CreateFirewallRule(TestRuleName, TestPort);
            createResult.Should().BeTrue();
            var getResult1 = sut.GetFirewallRule(TestRuleName);
            getResult1.Should().NotBeNull();
            getResult1.Rules.First().LocalPort.Should().Be(TestPort);

            var result = sut.UpdateFirewallRule(TestRuleName, TestPort2);

            result.Should().BeTrue();
            var getResult2 = sut.GetFirewallRule(TestRuleName);
            getResult2.Should().NotBeNull();
            getResult2.Rules.First().LocalPort.Should().Be(TestPort2);
        }

        public void Dispose()
        {
            if (!PermissionsHelper.IsRunAsAdministrator())
                return;

            var sut = new NetShHelper();
            sut.DeleteFirewallRule(TestRuleName);
        }
    }
}
