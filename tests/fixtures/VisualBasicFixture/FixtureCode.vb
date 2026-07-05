Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Runtime.CompilerServices
Imports System.Threading
Imports System.Threading.Tasks
Imports MediatR
Imports Microsoft.AspNetCore.Authorization
Imports Microsoft.AspNetCore.Builder
Imports Microsoft.AspNetCore.Mvc
Imports Microsoft.EntityFrameworkCore
Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports Microsoft.Extensions.Options
Imports Xunit

Namespace Xunit
    <AttributeUsage(AttributeTargets.Method)>
    Public NotInheritable Class FactAttribute
        Inherits Attribute
    End Class
End Namespace

Namespace Microsoft.AspNetCore.Authorization
    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method)>
    Public NotInheritable Class AuthorizeAttribute
        Inherits Attribute

        Public Property Policy As String
        Public Property Roles As String
        Public Property AuthenticationSchemes As String
    End Class

    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method)>
    Public NotInheritable Class AllowAnonymousAttribute
        Inherits Attribute
    End Class
End Namespace

Namespace Microsoft.AspNetCore.Mvc
    Public MustInherit Class ControllerBase
    End Class

    <AttributeUsage(AttributeTargets.Class)>
    Public NotInheritable Class ApiControllerAttribute
        Inherits Attribute
    End Class

    <AttributeUsage(AttributeTargets.Class Or AttributeTargets.Method)>
    Public NotInheritable Class RouteAttribute
        Inherits Attribute

        Public Sub New(template As String)
            Me.Template = template
        End Sub

        Public ReadOnly Property Template As String
    End Class

    <AttributeUsage(AttributeTargets.Method)>
    Public NotInheritable Class HttpGetAttribute
        Inherits Attribute

        Public Sub New(Optional template As String = Nothing)
            Me.Template = template
        End Sub

        Public ReadOnly Property Template As String
    End Class

    <AttributeUsage(AttributeTargets.Method)>
    Public NotInheritable Class NonActionAttribute
        Inherits Attribute
    End Class
End Namespace

Namespace Microsoft.AspNetCore.Builder
    Public NotInheritable Class WebApplication
    End Class

    Public Module EndpointRouteBuilderExtensions
        <Extension>
        Public Function MapGet(app As WebApplication, pattern As String, handler As Func(Of String)) As WebApplication
            Return app
        End Function

        <Extension>
        Public Function RequireAuthorization(app As WebApplication, Optional policy As String = Nothing) As WebApplication
            Return app
        End Function

        <Extension>
        Public Function AllowAnonymous(app As WebApplication) As WebApplication
            Return app
        End Function
    End Module
End Namespace

Namespace Microsoft.Extensions.DependencyInjection
    Public Interface IServiceCollection
    End Interface

    Public NotInheritable Class OptionsBuilder(Of TOptions As Class)
    End Class

    Public Module ServiceCollectionServiceExtensions
        <Extension>
        Public Function AddScoped(Of TService, TImplementation As TService)(services As IServiceCollection) As IServiceCollection
            Return services
        End Function

        <Extension>
        Public Function AddScoped(Of TService)(services As IServiceCollection) As IServiceCollection
            Return services
        End Function

        <Extension>
        Public Function AddSingleton(Of TService)(services As IServiceCollection) As IServiceCollection
            Return services
        End Function

        <Extension>
        Public Function AddHostedService(Of TService As Microsoft.Extensions.Hosting.IHostedService)(services As IServiceCollection) As IServiceCollection
            Return services
        End Function

        <Extension>
        Public Function Configure(Of TOptions)(services As IServiceCollection, configureOptions As Action(Of TOptions)) As IServiceCollection
            Return services
        End Function

        <Extension>
        Public Function AddOptions(Of TOptions As Class)(services As IServiceCollection) As OptionsBuilder(Of TOptions)
            Return New OptionsBuilder(Of TOptions)()
        End Function

        <Extension>
        Public Function Bind(Of TOptions As Class)(builder As OptionsBuilder(Of TOptions), configuration As IConfiguration) As OptionsBuilder(Of TOptions)
            Return builder
        End Function

        <Extension>
        Public Function ValidateDataAnnotations(Of TOptions As Class)(builder As OptionsBuilder(Of TOptions)) As OptionsBuilder(Of TOptions)
            Return builder
        End Function

        <Extension>
        Public Function ValidateOnStart(Of TOptions As Class)(builder As OptionsBuilder(Of TOptions)) As OptionsBuilder(Of TOptions)
            Return builder
        End Function
    End Module
End Namespace

Namespace Microsoft.Extensions.Configuration
    Public Interface IConfiguration
        Function GetSection(key As String) As IConfiguration
    End Interface
End Namespace

Namespace Microsoft.Extensions.Hosting
    Public Interface IHostedService
        Function StartAsync(cancellationToken As CancellationToken) As Task
    End Interface

    Public MustInherit Class BackgroundService
        Implements IHostedService

        Public Function StartAsync(cancellationToken As CancellationToken) As Task Implements IHostedService.StartAsync
            Return ExecuteAsync(cancellationToken)
        End Function

        Protected MustOverride Function ExecuteAsync(stoppingToken As CancellationToken) As Task
    End Class
End Namespace

Namespace Microsoft.Extensions.Options
    Public Interface IOptions(Of Out TOptions)
        ReadOnly Property Value As TOptions
    End Interface
End Namespace

Namespace MediatR
    Public Interface IRequest(Of Out TResponse)
    End Interface

    Public Interface IRequestHandler(Of In TRequest, TResponse)
        Function Handle(request As TRequest, cancellationToken As CancellationToken) As Task(Of TResponse)
    End Interface

    Public Interface ISender
        Function Send(Of TResponse)(request As IRequest(Of TResponse), Optional cancellationToken As CancellationToken = Nothing) As Task(Of TResponse)
    End Interface
End Namespace

Namespace Microsoft.EntityFrameworkCore
    Public Class DbContext
    End Class

    Public Class DbSet(Of TEntity)
        Inherits List(Of TEntity)
    End Class

    Public Interface IEntityTypeConfiguration(Of TEntity)
        Sub Configure(builder As EntityTypeBuilder(Of TEntity))
    End Interface

    Public NotInheritable Class EntityTypeBuilder(Of TEntity)
    End Class
End Namespace

Namespace System.Data.SqlClient
    Public NotInheritable Class SqlCommand
        Public Sub New(commandText As String)
        End Sub
    End Class
End Namespace

Namespace VisualBasicFixture
    Public Interface IWidgetStore
    End Interface

    Public NotInheritable Class SqlWidgetStore
        Implements IWidgetStore
    End Class

    Public NotInheritable Class WidgetService
        Private ReadOnly store As IWidgetStore

        Public Sub New(store As IWidgetStore)
            Me.store = store
        End Sub

        Public Function Format(count As Integer) As String
            Return $"widget:{count}"
        End Function
    End Class

    Public NotInheritable Class WidgetRunner
        Public Function Run(service As WidgetService) As String
            Return service.Format(3)
        End Function
    End Class

    Public NotInheritable Class RootSingleton
        Public Sub New(scoped As ScopedThing)
        End Sub
    End Class

    Public NotInheritable Class ScopedThing
    End Class

    Public NotInheritable Class Worker
        Inherits BackgroundService

        Protected Overrides Function ExecuteAsync(stoppingToken As CancellationToken) As Task
            Return Task.CompletedTask
        End Function
    End Class

    Public NotInheritable Class PaymentOptions
        Public Property Name As String = ""
    End Class

    Public NotInheritable Class PaymentService
        Public Sub New(options As IOptions(Of PaymentOptions))
        End Sub
    End Class

    Public Module CompositionRoot
        Public Sub Configure(services As IServiceCollection, configuration As IConfiguration)
            services.AddScoped(Of IWidgetStore, SqlWidgetStore)()
            services.AddScoped(Of ScopedThing)()
            services.AddSingleton(Of RootSingleton)()
            services.AddHostedService(Of Worker)()
            services.Configure(Of PaymentOptions)(Sub(options) options.Name = "fixture")
            services.AddOptions(Of PaymentOptions)().Bind(configuration.GetSection("Payments")).ValidateDataAnnotations().ValidateOnStart()
        End Sub
    End Module

    <ApiController>
    <Route("widgets")>
    Public NotInheritable Class WidgetsController
        Inherits ControllerBase

        <HttpGet("{id}")>
        <Authorize(Policy := "Widgets.Read")>
        Public Function GetWidget(id As Integer) As String
            Return WidgetHandler.Handle()
        End Function

        <NonAction>
        Public Function Helper() As String
            Return "helper"
        End Function
    End Class

    Public Module EndpointSetup
        Public Sub Map(app As WebApplication)
            app.MapGet("/widgets", AddressOf WidgetHandler.Handle).RequireAuthorization()
        End Sub
    End Module

    Public Module WidgetHandler
        Public Function Handle() As String
            Return "widget"
        End Function
    End Module

    Public NotInheritable Class CreateOrderCommand
        Implements IRequest(Of String)
    End Class

    Public NotInheritable Class CreateOrderHandler
        Implements IRequestHandler(Of CreateOrderCommand, String)

        Public Function Handle(request As CreateOrderCommand, cancellationToken As CancellationToken) As Task(Of String) Implements IRequestHandler(Of CreateOrderCommand, String).Handle
            Return Task.FromResult("created")
        End Function
    End Class

    Public NotInheritable Class MessageSender
        Public Function Dispatch(sender As ISender) As Task(Of String)
            Return sender.Send(New CreateOrderCommand())
        End Function

        Public Function DispatchLocal() As Task(Of String)
            Return Send(New CreateOrderCommand())
        End Function

        Private Function Send(request As CreateOrderCommand) As Task(Of String)
            Return Task.FromResult("sent")
        End Function
    End Class

    Public NotInheritable Class OrdersDbContext
        Inherits DbContext

        Public Property Orders As DbSet(Of Order) = New DbSet(Of Order)()
    End Class

    Public NotInheritable Class Order
        Public Property Id As Integer
    End Class

    Public NotInheritable Class OrderConfiguration
        Implements IEntityTypeConfiguration(Of Order)

        Public Sub Configure(builder As EntityTypeBuilder(Of Order)) Implements IEntityTypeConfiguration(Of Order).Configure
        End Sub
    End Class

    Public NotInheritable Class QuerySamples
        Public Function Find(context As OrdersDbContext) As Order
            Return context.Orders.FirstOrDefault()
        End Function
    End Class

    Public NotInheritable Class WidgetTests
        <Fact>
        Public Sub GetWidget_ReturnsWidget()
        End Sub
    End Class

    Public NotInheritable Class ReviewSamples
        Public Async Sub AsyncVoidSignal()
            Await Task.Delay(1)
        End Sub

        Public Sub SqlConstructionSignal(tableName As String)
            Dim command = New System.Data.SqlClient.SqlCommand("SELECT * FROM " & tableName)
        End Sub
    End Class
End Namespace
