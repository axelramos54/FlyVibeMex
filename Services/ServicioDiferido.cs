namespace FlyVibeMex.Services;

/// <summary>
/// Permite pedir cualquier servicio como <c>Lazy&lt;T&gt;</c> en un constructor (T8.1). El servicio de verdad se crea
/// la primera vez que se lee <c>.Value</c>, no al crear el controlador.
/// </summary>
/// <remarks>
/// Para qué sirve: los servicios que usan la BD reciben <c>FlyVibeMexDbContext</c>, y el DbContext lanza
/// "falta la cadena de conexión" en cuanto se crea (Program.cs). Con <c>Lazy&lt;IAuthService&gt;</c>, Inicio, Iniciar
/// sesión y Crear cuenta siguen abriendo en el "modo sin BD" (útil para quien solo trabaja en el diseño), y la BD
/// solo se toca cuando de verdad se necesita. Así ya ningún controlador usa <c>HttpContext.RequestServices</c>.
/// Registro (Program.cs): <c>builder.Services.AddTransient(typeof(Lazy&lt;&gt;), typeof(ServicioDiferido&lt;&gt;));</c>
/// El <see cref="IServiceProvider"/> que recibe es el de la petición, así que respeta los servicios Scoped.
/// </remarks>
public sealed class ServicioDiferido<T> : Lazy<T> where T : notnull
{
    public ServicioDiferido(IServiceProvider proveedor) : base(() => proveedor.GetRequiredService<T>())
    {
    }
}
