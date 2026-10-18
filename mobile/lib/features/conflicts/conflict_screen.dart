import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../data/local/local_database.dart';

/// Rejected mutations remain auditable. Only a duplicate KOT whose state is
/// verified against the desktop is moved to the reconciled history tab.
class ConflictScreen extends StatefulWidget {
  const ConflictScreen({
    required this.database,
    required this.strings,
    super.key,
  });

  final LocalDatabase database;
  final AppStrings strings;

  @override
  State<ConflictScreen> createState() => _ConflictScreenState();
}

class _ConflictScreenState extends State<ConflictScreen> {
  bool _showHistory = false;
  int _revision = 0;

  Future<void> _acknowledge(Map<String, Object?> conflict) async {
    final operation = conflict['operation']?.toString() ?? 'operation';
    final mutationId = conflict['mutation_id']?.toString();
    if (mutationId == null || mutationId.isEmpty) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Acknowledge rejected operation?'),
        content: Text(
          'The desktop rejected $operation. Confirm the order is correct '
          'on the desktop before acknowledging. This will hide the warning '
          'but will NOT send, apply, or retry the rejected action. '
          'The record stays in Reconciled history.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep warning'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Acknowledge'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    await widget.database.acknowledgeConflict(mutationId);
    if (mounted) setState(() => _revision++);
  }


  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.strings.conflicts)),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: SegmentedButton<bool>(
              segments: const [
                ButtonSegment(
                  value: false,
                  label: Text('Needs review'),
                  icon: Icon(Icons.warning_amber_rounded),
                ),
                ButtonSegment(
                  value: true,
                  label: Text('Reconciled history'),
                  icon: Icon(Icons.history),
                ),
              ],
              selected: <bool>{_showHistory},
              onSelectionChanged: (value) {
                setState(() => _showHistory = value.single);
              },
            ),
          ),
          Expanded(
            child: FutureBuilder<List<Map<String, Object?>>>(
              key: ValueKey('$_showHistory-$_revision'),
              future: widget.database.conflicts(archived: _showHistory),
              builder: (context, snapshot) {
                if (snapshot.hasError) {
                  return Center(
                    child: Text('Unable to load conflict history: ${snapshot.error}'),
                  );
                }
                final conflicts = snapshot.data;
                if (conflicts == null) {
                  return const Center(child: CircularProgressIndicator());
                }
                if (conflicts.isEmpty) {
                  return Center(
                    child: Text(
                      _showHistory
                          ? 'No reconciled conflicts.'
                          : 'No unresolved sync conflicts.',
                    ),
                  );
                }

                return ListView.separated(
                  padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
                  itemCount: conflicts.length,
                  separatorBuilder: (_, _) => const Divider(),
                  itemBuilder: (context, index) {
                    final conflict = conflicts[index];
                    return ListTile(
                      leading: Icon(
                        _showHistory
                            ? Icons.check_circle_outline
                            : Icons.warning_amber_rounded,
                        color: _showHistory ? Colors.green : Colors.orange,
                      ),
                      title: Text(
                        conflict['code']?.toString() ?? 'sync_conflict',
                      ),
                      subtitle: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(conflict['message']?.toString() ?? ''),
                          if (_showHistory)
                            Text(
                              conflict['resolution']?.toString() ??
                                  'Desktop reconciliation confirmed.',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                        ],
                      ),
                      trailing: _showHistory
                          ? Text(conflict['operation']?.toString() ?? '')
                          : IconButton(
                              icon: const Icon(Icons.task_alt_outlined),
                              tooltip: 'Review and acknowledge',
                              onPressed: () => _acknowledge(conflict),
                            ),
                      isThreeLine: _showHistory,
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}
