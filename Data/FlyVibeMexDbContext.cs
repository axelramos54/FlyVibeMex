using FlyVibeMex.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FlyVibeMex.Data;

/// <summary>
/// Contexto de Entity Framework Core de FlyVibe Mex (SQL Server).
/// Mapea las 26 clases de <c>Models/</c> a las tablas de <c>sql/00_db_prueba_conexion.sql</c> +
/// <c>sql/01_esquema.sql</c>: <b>cada propiedad se llama igual que su columna</b> (snake_case, con los nombres
/// nuevos de los modelos: <c>id_usuarioglobal</c>, <c>id_usuariog</c>, <c>fecha_inicio</c>…), por eso no se usa
/// <c>HasColumnName</c>. Incluye conversores globales para los tipos sin signo que SQL Server no tiene.
/// </summary>
/// <remarks>
/// <para><b>Regla anti SQL Injection (obligatoria para todo el equipo):</b></para>
/// <list type="bullet">
/// <item>Consultar SIEMPRE con LINQ (<c>Where</c>, <c>Select</c>, <c>FirstOrDefaultAsync</c>…). EF Core convierte cada
/// valor en un parámetro SQL (<c>@__correo_0</c>); el valor nunca se pega dentro del texto SQL.</item>
/// <item><b>PROHIBIDO</b> usar <c>FromSqlRaw</c>, <c>ExecuteSqlRaw</c> o <c>SqlQueryRaw</c> con interpolación
/// (<c>$"...{x}"</c>) o concatenación (<c>"..." + x</c>) de datos que vengan del usuario.</item>
/// <item>Si algún día se necesita SQL manual o un procedimiento almacenado, usar únicamente
/// <c>FromSql</c>/<c>FromSqlInterpolated</c>/<c>ExecuteSqlInterpolated</c> (convierten cada <c>{x}</c> en parámetro)
/// o <c>FromSqlRaw</c> con objetos <c>SqlParameter</c> explícitos.</item>
/// </list>
/// <para>Las clases de <c>Models/</c> no tienen propiedades de navegación: las relaciones se resuelven con
/// joins LINQ por llave foránea (<c>id_rol</c>, <c>id_usuarioglobal</c>, <c>id_servicio</c>…). Aun así, todas las FK
/// del script están declaradas abajo con <c>HasOne&lt;T&gt;().WithMany()</c>.</para>
/// <para>Si una base de datos se creó con un script anterior (tabla <c>UsuariosGlobales</c>, columnas
/// <c>tipo_prooverdor</c>, <c>creado</c>, <c>id_usuario</c>…), ejecutar <c>sql/03_alinear_nombres_legado.sql</c>.</para>
/// </remarks>
public class FlyVibeMexDbContext : DbContext
{
    public FlyVibeMexDbContext(DbContextOptions<FlyVibeMexDbContext> options) : base(options)
    {
    }

    // ===================== Usuarios =====================
    public DbSet<RolesUsuario> RolesUsuarios => Set<RolesUsuario>();
    public DbSet<UsuariosGlobales> Usuarios => Set<UsuariosGlobales>();
    public DbSet<Ubicaciones> Ubicaciones => Set<Ubicaciones>();

    // ===================== Catálogo / servicios =====================
    public DbSet<Proveedores> Proveedores => Set<Proveedores>();
    public DbSet<Servicios> Servicios => Set<Servicios>();
    public DbSet<Hospedajes> Hospedajes => Set<Hospedajes>();
    public DbSet<Vehiculos> Vehiculos => Set<Vehiculos>();
    public DbSet<Vuelos> Vuelos => Set<Vuelos>();
    public DbSet<Paquetes> Paquetes => Set<Paquetes>();
    public DbSet<PaqueteServicio> PaquetesServicios => Set<PaqueteServicio>();
    public DbSet<DisponibilidadServicio> DisponibilidadServicios => Set<DisponibilidadServicio>();

    // ===================== Promociones =====================
    public DbSet<Promociones> Promociones => Set<Promociones>();
    public DbSet<PromocionServicio> PromocionesServicios => Set<PromocionServicio>();

    // ===================== Reservaciones =====================
    public DbSet<EstadosReservacion> EstadosReservacion => Set<EstadosReservacion>();
    public DbSet<Reservaciones> Reservaciones => Set<Reservaciones>();
    public DbSet<DetallesReservacion> DetallesReservacion => Set<DetallesReservacion>();
    public DbSet<HistorialEstadoReservacion> HistorialEstadosReservacion => Set<HistorialEstadoReservacion>();

    // ===================== Pagos / reembolsos =====================
    public DbSet<MetodosPago> MetodosPago => Set<MetodosPago>();
    public DbSet<InstitucionesFinancieras> InstitucionesFinancieras => Set<InstitucionesFinancieras>();
    public DbSet<TarjetasUsuarios> TarjetasUsuarios => Set<TarjetasUsuarios>();
    public DbSet<EstadosPago> EstadosPago => Set<EstadosPago>();
    public DbSet<Pagos> Pagos => Set<Pagos>();
    public DbSet<HistorialEstadoPago> HistorialEstadosPago => Set<HistorialEstadoPago>();
    public DbSet<Reembolsos> Reembolsos => Set<Reembolsos>();
    public DbSet<HistorialEstadoReembolso> HistorialEstadosReembolso => Set<HistorialEstadoReembolso>();

    // ===================== Soporte =====================
    public DbSet<SolicitudesSoporte> SolicitudesSoporte => Set<SolicitudesSoporte>();

    /// <summary>
    /// Convenciones globales aplicadas a TODAS las entidades antes de construir el modelo.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server no tiene enteros sin signo. Los modelos usan ulong/ushort, pero las columnas son
        // bigint/smallint CON signo. Sin estos conversores EF Core mapearía ulong a decimal(20,0) y fallaría
        // al leer/escribir. Los IDs nunca superan long.MaxValue, así que la conversión es segura.
        configurationBuilder.Properties<ulong>().HaveConversion<long>();   // ulong  <-> bigint
        configurationBuilder.Properties<ushort>().HaveConversion<short>(); // ushort <-> smallint

        // Dinero: decimal(12,2) por defecto (regla del proyecto: nunca double/float para importes).
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);

        // El script T-SQL del equipo usa `datetime` (no `datetime2`).
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigurarUsuarios(modelBuilder);
        ConfigurarCatalogo(modelBuilder);
        ConfigurarPromociones(modelBuilder);
        ConfigurarReservaciones(modelBuilder);
        ConfigurarPagos(modelBuilder);
        ConfigurarSoporte(modelBuilder);

        // Fechas de control (creado_en, actualizada_en, fecha_cambio…): si el código no las asigna,
        // las llena SQL Server con CURRENT_TIMESTAMP (evita insertar 0001-01-01, que `datetime` no admite).
        AplicarFechasDeControlPorDefecto(modelBuilder);

        // Red de seguridad: si en el futuro algún modelo usa ulong? / ushort? (FK opcionales), se les aplica
        // el mismo conversor que a ulong / ushort.
        AplicarConversoresSinSignoNullables(modelBuilder);
    }


    /// <summary>Longitud de <c>imagen_ruta</c> (NVARCHAR(260)) en usuarios, hospedajes, vehiculos y paquete (T2.5).</summary>
    private const int LongitudRutaImagen = 260;

    // ---------------------------------------------------------------------------------------------
    // Usuarios, roles y ubicaciones
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarUsuarios(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RolesUsuario>(e =>
        {
            e.ToTable("roles_usuarios");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.nombre).HasMaxLength(30).IsUnicode(false);
            e.Property(x => x.descripcion).HasMaxLength(150).IsUnicode(false).IsRequired(false);
            e.HasIndex(x => x.nombre).IsUnique();
        });

        modelBuilder.Entity<UsuariosGlobales>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();

            // IsUnicode(false) = varchar, igual que la columna. Así el parámetro de la consulta de login
            // también es varchar(150) y SQL Server puede usar el índice único de `correo` (sin conversión implícita).
            e.Property(x => x.nombre).HasMaxLength(80).IsUnicode(false);
            e.Property(x => x.apellido_paterno).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.apellido_materno).HasMaxLength(60).IsUnicode(false);
            e.Property(x => x.correo).HasMaxLength(150).IsUnicode(false);
            e.Property(x => x.telefono).HasMaxLength(20).IsUnicode(false);
            e.Property(x => x.password_hash).HasMaxLength(255).IsUnicode(false);
            e.Property(x => x.imagen_ruta).HasMaxLength(LongitudRutaImagen);   // NVARCHAR(260) NULL
            e.Property(x => x.creado_en).HasDefaultValueSql("CURRENT_TIMESTAMP");

            e.HasIndex(x => x.correo).IsUnique();
            e.HasIndex(x => x.telefono).IsUnique();

            // usuarios.id_rol -> roles_usuarios.id
            e.HasOne<RolesUsuario>()
                .WithMany()
                .HasForeignKey(x => x.id_rol)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Ubicaciones>(e =>
        {
            e.ToTable("ubicaciones");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.pais).HasMaxLength(60);
            e.Property(x => x.estado).HasMaxLength(60);
            e.Property(x => x.municipio_ciudad).HasMaxLength(100);
            e.Property(x => x.codigo_postal).HasMaxLength(10);

            // FIXME (modelo): `correo` no existe en la tabla ubicaciones del script; se ignora para que EF no la pida.
            e.Ignore(x => x.correo);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Proveedores, servicios y subtipos
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarCatalogo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Proveedores>(e =>
        {
            e.ToTable("proveedores");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.tipo_proveedor).HasMaxLength(20);
            e.Property(x => x.nombre).HasMaxLength(120);
            e.Property(x => x.telefono).HasMaxLength(20);
            e.Property(x => x.correo).HasMaxLength(150);
            e.Property(x => x.id_ubicacion).IsRequired(false);

            // proveedores.id_ubicacion (opcional) -> ubicaciones.id
            e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_ubicacion).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Servicios>(e =>
        {
            e.ToTable("servicios");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.tipo_servicio).HasMaxLength(20);
            e.Property(x => x.nombre).HasMaxLength(150);
            e.Property(x => x.descripcion).HasMaxLength(2000).IsRequired(false);
            e.Property(x => x.moneda).HasMaxLength(3);
            // precio_base usa la precisión global decimal(12,2), igual que la columna del esquema.
            // Política de cancelación. El porcentaje es DECIMAL(5,2), no el (12,2) global.
            e.Property(x => x.porcentaje_reembolso_tardio).HasPrecision(5, 2);
            e.Property(x => x.politica_cancelacion).HasMaxLength(500);

            // servicios.id_proveedor -> proveedores.id
            e.HasOne<Proveedores>().WithMany().HasForeignKey(x => x.id_proveedor).OnDelete(DeleteBehavior.Restrict);
        });

        // Subtipos: la llave es `id_servicio`, que es a la vez FK hacia servicios.id (relación 1:1).
        // NO es identity: primero se inserta el servicio y luego el subtipo con el MISMO id. Por eso
        // ValueGeneratedNever (con ValueGeneratedOnAdd EF no enviaría el id y el INSERT fallaría).
        modelBuilder.Entity<Vuelos>(e =>
        {
            e.ToTable("vuelos");
            e.HasKey(x => x.id_servicio);
            e.Property(x => x.id_servicio).ValueGeneratedNever();
            e.Property(x => x.codigo_vuelo).HasMaxLength(10);
            e.Property(x => x.clase).HasMaxLength(20);

            e.HasOne<Servicios>().WithOne().HasForeignKey<Vuelos>(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_origen).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_destino).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Hospedajes>(e =>
        {
            e.ToTable("hospedajes");
            e.HasKey(x => x.id_servicio);
            e.Property(x => x.id_servicio).ValueGeneratedNever();
            e.Property(x => x.tipo_habitacion).HasMaxLength(60);
            e.Property(x => x.categoria).HasMaxLength(40).IsRequired(false);
            e.Property(x => x.imagen_ruta).HasMaxLength(LongitudRutaImagen);

            e.HasOne<Servicios>().WithOne().HasForeignKey<Hospedajes>(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_ubicacion).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehiculos>(e =>
        {
            e.ToTable("vehiculos");
            e.HasKey(x => x.id_servicio);
            e.Property(x => x.id_servicio).ValueGeneratedNever();
            e.Property(x => x.categoria).HasMaxLength(50);
            e.Property(x => x.marca).HasMaxLength(50);
            e.Property(x => x.modelo).HasMaxLength(60);
            e.Property(x => x.imagen_ruta).HasMaxLength(LongitudRutaImagen);

            e.HasOne<Servicios>().WithOne().HasForeignKey<Vehiculos>(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ubicaciones>().WithMany().HasForeignKey(x => x.id_ubicacion_retiro).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Paquetes>(e =>
        {
            e.ToTable("paquete");
            e.HasKey(x => x.id_servicio);
            e.Property(x => x.id_servicio).ValueGeneratedNever();
            e.Property(x => x.imagen_ruta).HasMaxLength(LongitudRutaImagen);

            e.HasOne<Servicios>().WithOne().HasForeignKey<Paquetes>(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaqueteServicio>(e =>
        {
            // id_servicio = el paquete (FK -> paquete.id_servicio); id_servicio_incluido = lo que trae el paquete.
            e.ToTable("paquete_servicio");
            e.HasKey(x => new { x.id_servicio, x.id_servicio_incluido });
            e.Property(x => x.id_servicio).ValueGeneratedNever();
            e.Property(x => x.id_servicio_incluido).ValueGeneratedNever();
            e.Property(x => x.cantidad).HasPrecision(8, 2);

            e.HasOne<Paquetes>().WithMany().HasForeignKey(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Servicios>().WithMany().HasForeignKey(x => x.id_servicio_incluido).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DisponibilidadServicio>(e =>
        {
            e.ToTable("disponibilidad_servicio");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            // Regla R3: una sola fila por servicio y día.
            e.HasIndex(x => new { x.id_servicio, x.fecha }).IsUnique();

            e.HasOne<Servicios>().WithMany().HasForeignKey(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Promociones
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarPromociones(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Promociones>(e =>
        {
            e.ToTable("promociones");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.codigo).HasMaxLength(30);
            e.Property(x => x.nombre).HasMaxLength(120);
            e.Property(x => x.descripcion).HasMaxLength(500).IsRequired(false);
            e.Property(x => x.tipo_descuento).HasMaxLength(15);
            e.HasIndex(x => x.codigo).IsUnique();
        });

        modelBuilder.Entity<PromocionServicio>(e =>
        {
            e.ToTable("promocion_servicio");
            e.HasKey(x => new { x.id_promocion, x.id_servicio });

            e.HasOne<Promociones>().WithMany().HasForeignKey(x => x.id_promocion).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Servicios>().WithMany().HasForeignKey(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Reservaciones
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarReservaciones(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EstadosReservacion>(e =>
        {
            e.ToTable("estados_reservacion");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.nombre).HasMaxLength(30);
            e.Property(x => x.descripcion).HasMaxLength(150).IsRequired(false);
            e.HasIndex(x => x.nombre).IsUnique();
        });

        modelBuilder.Entity<Reservaciones>(e =>
        {
            // Columna del titular: `id_usuarioglobal` (FK -> usuarios.id).
            e.ToTable("reservaciones");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.codigo_reserva).HasMaxLength(30);
            e.Property(x => x.moneda).HasMaxLength(3);
            e.HasIndex(x => x.codigo_reserva).IsUnique();

            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_usuarioglobal).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstadosReservacion>().WithMany().HasForeignKey(x => x.id_estado_reservacion).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DetallesReservacion>(e =>
        {
            e.ToTable("detalle_reservacion");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.cantidad).HasPrecision(8, 2);
            e.Property(x => x.descripcion_snapshot).HasMaxLength(250);
            // Snapshot de la política de cancelación.
            e.Property(x => x.porcentaje_reembolso_tardio).HasPrecision(5, 2);
            e.Property(x => x.politica_cancelacion).HasMaxLength(500);

            e.HasOne<Reservaciones>().WithMany().HasForeignKey(x => x.id_reservacion).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Servicios>().WithMany().HasForeignKey(x => x.id_servicio).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Promociones>().WithMany().HasForeignKey(x => x.id_promocion).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistorialEstadoReservacion>(e =>
        {
            // Bitácora de la regla R6. id_usuario_cambio = NULL => cambio automático del sistema.
            e.ToTable("historial_estado_reservacion");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.motivo).HasMaxLength(250);

            e.HasOne<Reservaciones>().WithMany().HasForeignKey(x => x.id_reservacion).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstadosReservacion>().WithMany().HasForeignKey(x => x.id_estado_reservacion).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_usuario_cambio).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Pagos y reembolsos
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarPagos(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetodosPago>(e =>
        {
            e.ToTable("metodos_pago");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.nombre).HasMaxLength(40);
            e.Property(x => x.descripcion).HasMaxLength(150).IsRequired(false);
            e.HasIndex(x => x.nombre).IsUnique();
        });

        modelBuilder.Entity<InstitucionesFinancieras>(e =>
        {
            e.ToTable("instituciones_financieras");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.nombre).HasMaxLength(120).IsRequired(false);
            e.Property(x => x.clave_institucion).HasMaxLength(10).IsRequired(false);
            e.Property(x => x.codigo_internacional).HasMaxLength(15).IsRequired(false);
            e.Property(x => x.telefono).HasMaxLength(20).IsRequired(false);
        });

        modelBuilder.Entity<TarjetasUsuarios>(e =>
        {
            // Regla R5: solo token del gateway + últimos 4 dígitos. Jamás PAN completo ni CVV.
            // Columna del titular: `id_usuariog` (así se llama la propiedad del modelo).
            e.ToTable("tarjetas_usuarios");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.token_referencia).HasMaxLength(100);
            e.Property(x => x.marca).HasMaxLength(20);
            e.Property(x => x.ultimos4).HasMaxLength(4);
            e.Property(x => x.alias).HasMaxLength(40).IsRequired(false);
            e.HasIndex(x => x.token_referencia).IsUnique();

            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_usuariog).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<InstitucionesFinancieras>().WithMany().HasForeignKey(x => x.id_institucion).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EstadosPago>(e =>
        {
            e.ToTable("estados_pago");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.nombre).HasMaxLength(30);
            e.Property(x => x.descripcion).HasMaxLength(150).IsRequired(false);
            e.HasIndex(x => x.nombre).IsUnique();
        });

        modelBuilder.Entity<Pagos>(e =>
        {
            e.ToTable("pagos");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.moneda).HasMaxLength(3);
            e.Property(x => x.referencia_externa).HasMaxLength(100).IsRequired(false);

            e.HasOne<Reservaciones>().WithMany().HasForeignKey(x => x.id_reservacion).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MetodosPago>().WithMany().HasForeignKey(x => x.id_metodo_pago).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TarjetasUsuarios>().WithMany().HasForeignKey(x => x.id_tarjeta).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstadosPago>().WithMany().HasForeignKey(x => x.id_estado_pago).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistorialEstadoPago>(e =>
        {
            e.ToTable("historial_estado_pago");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.detalle).HasMaxLength(250).IsRequired(false);

            e.HasOne<Pagos>().WithMany().HasForeignKey(x => x.id_pago).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EstadosPago>().WithMany().HasForeignKey(x => x.id_estado_pago).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Reembolsos>(e =>
        {
            e.ToTable("reembolsos");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.motivo).HasMaxLength(250);
            e.Property(x => x.estado).HasMaxLength(15);
            e.Property(x => x.referencia_externa).HasMaxLength(100).IsRequired(false);

            // Regla R8: el reembolso deriva del PAGO, no de la reservación.
            e.HasOne<Pagos>().WithMany().HasForeignKey(x => x.id_pago).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HistorialEstadoReembolso>(e =>
        {
            e.ToTable("historial_estado_reembolso");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.estado).HasMaxLength(15);
            e.Property(x => x.detalle).HasMaxLength(250).IsRequired(false);

            e.HasOne<Reembolsos>().WithMany().HasForeignKey(x => x.id_reembolso).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_usuario_cambio).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Soporte
    // ---------------------------------------------------------------------------------------------
    private static void ConfigurarSoporte(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SolicitudesSoporte>(e =>
        {
            // Tickets de la regla R9 (también los crea el bot, ver docs/06). Titular: `id_usuarioglobal`.
            e.ToTable("solicitudes_soporte");
            e.HasKey(x => x.id);
            e.Property(x => x.id).ValueGeneratedOnAdd();
            e.Property(x => x.tipo).HasMaxLength(20);
            e.Property(x => x.asunto).HasMaxLength(120);
            e.Property(x => x.mensaje).HasMaxLength(2000);
            e.Property(x => x.estado).HasMaxLength(20);

            // Dos FK distintas hacia usuarios: el cliente que abre el ticket y el agente que lo atiende.
            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_usuarioglobal).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UsuariosGlobales>().WithMany().HasForeignKey(x => x.id_agente_asignado).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Reservaciones>().WithMany().HasForeignKey(x => x.id_reservacion).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>Nombres de las columnas de fecha que SQL Server llena con <c>CURRENT_TIMESTAMP</c> si no se asignan.</summary>
    private static readonly string[] ColumnasFechaControl =
    {
        "creado_en", "creada_en", "actualizado_en", "actualizada_en", "fecha_cambio", "fecha_solicitud"
    };

    /// <summary>
    /// Marca las fechas de control (<see cref="ColumnasFechaControl"/>) con <c>HasDefaultValueSql("CURRENT_TIMESTAMP")</c>.
    /// Así un <c>Add(...)</c> que no asigna <c>creado_en</c> no intenta guardar <c>DateTime.MinValue</c> (0001-01-01), que la
    /// columna <c>datetime</c> rechaza. Si el código SÍ asigna la fecha, se guarda la que se asignó.
    /// </summary>
    private static void AplicarFechasDeControlPorDefecto(ModelBuilder modelBuilder)
    {
        var fechas = modelBuilder.Model.GetEntityTypes()
            .SelectMany(entidad => entidad.GetProperties()
                .Where(p => p.ClrType == typeof(DateTime) && ColumnasFechaControl.Contains(p.Name))
                .Select(p => (entidad.ClrType, p.Name)))
            .ToList();

        foreach (var (tipo, nombre) in fechas)
        {
            modelBuilder.Entity(tipo).Property(nombre).HasDefaultValueSql("CURRENT_TIMESTAMP");
        }
    }

    /// <summary>
    /// Aplica <c>ulong? &lt;-&gt; long?</c> y <c>ushort? &lt;-&gt; short?</c> a las propiedades nullables que no
    /// tengan ya un conversor (FK opcionales como <c>id_reservacion</c>, <c>id_agente_asignado</c>, <c>id_promocion</c>,
    /// <c>id_usuario_cambio</c>, <c>orden</c>…).
    /// </summary>
    private static void AplicarConversoresSinSignoNullables(ModelBuilder modelBuilder)
    {
        var ulongALong = new ValueConverter<ulong, long>(v => (long)v, v => (ulong)v);
        var ushortAShort = new ValueConverter<ushort, short>(v => (short)v, v => (ushort)v);

        foreach (var entidad in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propiedad in entidad.GetProperties())
            {
                if (propiedad.GetValueConverter() is not null || propiedad.GetProviderClrType() is not null)
                {
                    continue;
                }

                if (propiedad.ClrType == typeof(ulong?))
                {
                    propiedad.SetValueConverter(ulongALong);
                }
                else if (propiedad.ClrType == typeof(ushort?))
                {
                    propiedad.SetValueConverter(ushortAShort);
                }
            }
        }
    }
}
