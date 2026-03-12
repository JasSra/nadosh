using Microsoft.EntityFrameworkCore;
using Nadosh.Core.Models;

namespace Nadosh.Infrastructure.Data;

public class NadoshDbContext : DbContext
{
    public NadoshDbContext(DbContextOptions<NadoshDbContext> options) : base(options) { }

    public DbSet<EdgeAgent> EdgeAgents => Set<EdgeAgent>();
    public DbSet<EdgeSite> EdgeSites => Set<EdgeSite>();
    public DbSet<EdgeTaskExecutionRecord> EdgeTaskExecutionRecords => Set<EdgeTaskExecutionRecord>();
    public DbSet<AuthorizedTask> AuthorizedTasks => Set<AuthorizedTask>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AssessmentRun> AssessmentRuns => Set<AssessmentRun>();
    public DbSet<CertificateObservation> CertificateObservations => Set<CertificateObservation>();
    public DbSet<CurrentExposure> CurrentExposures => Set<CurrentExposure>();
    public DbSet<EnrichmentResult> EnrichmentResults => Set<EnrichmentResult>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<ObservationHandoffDispatch> ObservationHandoffDispatches => Set<ObservationHandoffDispatch>();
    public DbSet<RuleConfig> RuleConfigs => Set<RuleConfig>();
    public DbSet<ScanRun> ScanRuns => Set<ScanRun>();
    public DbSet<Stage1Dispatch> Stage1Dispatches => Set<Stage1Dispatch>();
    public DbSet<SuppressionRule> SuppressionRules => Set<SuppressionRule>();
    public DbSet<Target> Targets => Set<Target>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("AuditEvents");
            b.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AssessmentRun>(b =>
        {
            b.ToTable("AssessmentRuns");
            b.HasKey(e => e.RunId);
            b.HasIndex(e => e.Status);
            b.HasIndex(e => e.CreatedAt);
            b.HasIndex(e => e.ToolId);
            b.HasIndex(e => e.RequestedBy);
            b.Property(e => e.RunId).HasMaxLength(128);
            b.Property(e => e.ToolId).HasMaxLength(128);
            b.Property(e => e.RequestedBy).HasMaxLength(256);
            b.Property(e => e.TargetScope).HasMaxLength(512);
            b.Property(e => e.ApprovalReference).HasMaxLength(128);
            b.Property(e => e.ScopeKind)
                .HasConversion<string>()
                .HasMaxLength(64);
            b.Property(e => e.Environment)
                .HasConversion<string>()
                .HasMaxLength(64);
            b.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(64);
        });

        modelBuilder.Entity<EdgeSite>(b =>
        {
            b.ToTable("EdgeSites");
            b.HasKey(e => e.SiteId);
            b.HasIndex(e => e.Name);
            b.Property(e => e.SiteId).HasMaxLength(128);
            b.Property(e => e.Name).HasMaxLength(256);
            b.PrimitiveCollection(e => e.AllowedCidrs).HasColumnType("text[]");
            b.PrimitiveCollection(e => e.AllowedCapabilities).HasColumnType("text[]");
        });

        modelBuilder.Entity<EdgeAgent>(b =>
        {
            b.ToTable("EdgeAgents");
            b.HasKey(e => e.AgentId);
            b.HasIndex(e => e.SiteId);
            b.HasIndex(e => e.Status);
            b.HasIndex(e => e.LastSeenAt);
            b.Property(e => e.AgentId).HasMaxLength(128);
            b.Property(e => e.SiteId).HasMaxLength(128);
            b.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            b.PrimitiveCollection(e => e.AdvertisedCapabilities).HasColumnType("text[]");
            b.HasOne<EdgeSite>()
                .WithMany()
                .HasForeignKey(e => e.SiteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EdgeTaskExecutionRecord>(b =>
        {
            b.ToTable("EdgeTaskExecutionRecords");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.AuthorizedTaskId).IsUnique();
            b.HasIndex(e => e.Status);
            b.HasIndex(e => e.NextUploadAttemptAt);
            b.Property(e => e.AuthorizedTaskId).HasMaxLength(128);
            b.Property(e => e.SiteId).HasMaxLength(128);
            b.Property(e => e.AgentId).HasMaxLength(128);
            b.Property(e => e.TaskKind).HasMaxLength(128);
            b.Property(e => e.LeaseToken).HasMaxLength(128);
            b.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
        });

        modelBuilder.Entity<AuthorizedTask>(b =>
        {
            b.ToTable("AuthorizedTasks");
            b.HasKey(e => e.TaskId);
            b.HasIndex(e => new { e.SiteId, e.Status });
            b.HasIndex(e => e.AgentId);
            b.HasIndex(e => e.ClaimedByAgentId);
            b.HasIndex(e => e.ExpiresAt);
            b.HasIndex(e => e.LeaseExpiresAt);
            b.Property(e => e.TaskId).HasMaxLength(128);
            b.Property(e => e.SiteId).HasMaxLength(128);
            b.Property(e => e.AgentId).HasMaxLength(128);
            b.Property(e => e.ClaimedByAgentId).HasMaxLength(128);
            b.Property(e => e.TaskKind).HasMaxLength(128);
            b.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(32);
            b.PrimitiveCollection(e => e.RequiredCapabilities).HasColumnType("text[]");
            b.HasOne<EdgeSite>()
                .WithMany()
                .HasForeignKey(e => e.SiteId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EdgeAgent>()
                .WithMany()
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CertificateObservation>(b =>
        {
            b.ToTable("CertificateObservations");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.Sha256);
            b.HasIndex(e => e.Subject);
            b.PrimitiveCollection(e => e.SanList).HasColumnType("text[]");
        });

        modelBuilder.Entity<CurrentExposure>(b =>
        {
            b.ToTable("CurrentExposures");
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.TargetId, e.Port, e.Protocol }).IsUnique();
        });

        modelBuilder.Entity<EnrichmentResult>(b =>
        {
            b.ToTable("EnrichmentResults");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.ObservationId);
            b.HasIndex(e => e.CurrentExposureId);
            b.PrimitiveCollection(e => e.Tags).HasColumnType("text[]");
        });

        modelBuilder.Entity<Observation>(b =>
        {
            b.ToTable("Observations");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.ObservedAt);
            b.HasIndex(e => e.PipelineState);
            b.HasIndex(e => e.TargetId);
            b.Property(e => e.PipelineState)
                .HasConversion<string>()
                .HasMaxLength(64);
        });

        modelBuilder.Entity<ObservationHandoffDispatch>(b =>
        {
            b.ToTable("ObservationHandoffDispatches");
            b.HasKey(e => new { e.DispatchKind, e.SourceObservationId });
            b.HasIndex(e => e.ProducedObservationId);
            b.HasIndex(e => e.ScheduledAt);
            b.HasIndex(e => e.State);
            b.Property(e => e.DispatchKind)
                .HasConversion<string>()
                .HasMaxLength(64);
            b.Property(e => e.State)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(64);
        });

        modelBuilder.Entity<RuleConfig>(b =>
        {
            b.ToTable("RuleConfigs");
            b.HasKey(e => new { e.RuleId, e.Version });
        });

        modelBuilder.Entity<ScanRun>(b =>
        {
            b.ToTable("ScanRuns");
            b.HasKey(e => e.RunId);
        });

        modelBuilder.Entity<Stage1Dispatch>(b =>
        {
            b.ToTable("Stage1Dispatches");
            b.HasKey(e => new { e.BatchId, e.TargetIp });
            b.HasIndex(e => e.ScheduledAt);
            b.HasIndex(e => e.State);
            b.Property(e => e.State)
                .HasConversion<string>()
                .IsRequired()
                .HasMaxLength(64);
        });

        modelBuilder.Entity<SuppressionRule>(b =>
        {
            b.ToTable("SuppressionRules");
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.TargetIp);
        });

        modelBuilder.Entity<Target>(b =>
        {
            b.ToTable("Targets");
            b.HasKey(e => e.Ip);
            b.Property(e => e.Ip).HasMaxLength(45);
            b.HasIndex(e => e.NextScheduled);
            b.PrimitiveCollection(e => e.OwnershipTags).HasColumnType("text[]");
        });
    }
}
