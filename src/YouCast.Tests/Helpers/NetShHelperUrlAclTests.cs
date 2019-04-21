using System;
using System.Linq;
using FluentAssertions;
using Xunit;
using YouCast.Helpers;

namespace YouCast.Tests.Helpers
{
    public class NetShHelperUrlAclTests : IDisposable
    {
        private const string TestUrl = "http://+:2345/";

        [SkippableFact]
        public void ShouldBeAbleToCreateUrlAcl()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();

            var result = sut.CreateUrlAcl(TestUrl);

            result.Should().BeTrue();
        }

        [SkippableFact]
        public void ShouldBeAbleToGetUrlAcl()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();
            var createResult = sut.CreateUrlAcl(TestUrl);
            createResult.Should().BeTrue();

            var result = sut.GetUrlAcl(TestUrl);

            result.Should().NotBeNull();
            result.Reservations.First().Url.Should().Be(TestUrl);
            result.Reservations.First().Data["User"].Should().Be($"{Environment.UserDomainName}\\{Environment.UserName}");
        }

        [SkippableFact]
        public void ShouldBeAbleToDeleteUrlAcl()
        {
            Skip.If(!PermissionsHelper.IsRunAsAdministrator(), "require administrator permissions");
            var sut = new NetShHelper();
            var createResult = sut.CreateUrlAcl(TestUrl);
            createResult.Should().BeTrue();

            var result = sut.DeleteUrlAcl(TestUrl);

            result.Should().BeTrue();
            var getResult = sut.GetUrlAcl(TestUrl);
            getResult.Should().NotBeNull();
            getResult.Reservations.Should().BeEmpty();
        }

        public void Dispose()
        {
            if (!PermissionsHelper.IsRunAsAdministrator())
                return;

            var sut = new NetShHelper();
            sut.DeleteUrlAcl(TestUrl);
        }
    }
}