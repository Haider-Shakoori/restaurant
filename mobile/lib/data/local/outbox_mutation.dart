class OutboxMutation {
  const OutboxMutation({
    required this.id,
    required this.mutationId,
    required this.operation,
    required this.payload,
    required this.occurredAt,
    required this.attempts,
  });

  final int id;
  final String mutationId;
  final String operation;
  final Map<String, Object?> payload;
  final DateTime occurredAt;
  final int attempts;

  Map<String, Object?> toApiJson() {
    return <String, Object?>{
      'mutation_id': mutationId,
      'operation': operation,
      'occurred_at': occurredAt.toUtc().toIso8601String(),
      'payload': payload,
    };
  }
}
