using System.Text.Json.Serialization;

using FluentValidation;

using PaymentGateway.Api.Clients;

using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<PaymentsService>();

builder.Services.AddValidatorsFromAssemblyContaining<PostPaymentRequestValidator>();


var acquiringBankBaseUrl = builder.Configuration["AcquiringBank:BaseUrl"]
    ?? throw new InvalidOperationException("AcquiringBank:BaseUrl must be configured.");

builder.Services.AddHttpClient<IAcquiringBankClient, AcquiringBankClient>(client =>
{
    client.BaseAddress = new Uri(acquiringBankBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
