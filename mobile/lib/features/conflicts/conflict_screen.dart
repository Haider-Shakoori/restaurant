import 'package:flutter/material.dart';

import '../../app/app_strings.dart';
import '../../data/local/local_database.dart';

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
  late Future<List<Map<String, Object?>>> _conflicts;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _conflicts = widget.database.conflicts();
  }

  void _reload() {
    setState(() {
      _conflicts = widget.database.conflicts();
    });
  }

  Future<void> _run(
    Future<void> Function(String mutationId) action,
    String mutationId,
  ) async {
    if (_busy) {
      return;
    }

    setState(() => _busy = true);

    try {
      await action(mutationId);
    } finally {
      if (mounted) {
        setState(() => _busy = false);
        _reload();
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.strings.conflicts)),
      body: FutureBuilder<List<Map<String, Object?>>>(
        future: _conflicts,
        builder: (context, snapshot) {
          final conflicts = snapshot.data;

          if (conflicts == null) {
            return const Center(child: CircularProgressIndicator());
          }

          if (conflicts.isEmpty) {
            return const Center(child: Text('No sync conflicts.'));
          }

          return ListView.separated(
            padding: const EdgeInsets.all(16),
            itemCount: conflicts.length,
            separatorBuilder: (_, _) => const Divider(),
            itemBuilder: (context, index) {
              final conflict = conflicts[index];
              final mutationId = conflict['mutation_id']!.toString();

              return ListTile(
                leading: const Icon(Icons.warning_amber),
                title: Text(conflict['code']!.toString()),
                subtitle: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(conflict['message']!.toString()),
                    Text(
                      conflict['operation']!.toString(),
                      style: Theme.of(context).textTheme.labelSmall,
                    ),
                  ],
                ),
                trailing: Wrap(
                  spacing: 4,
                  children: [
                    IconButton(
                      tooltip: 'Retry',
                      icon: const Icon(Icons.refresh),
                      onPressed: _busy
                          ? null
                          : () =>
                                _run(widget.database.retryMutation, mutationId),
                    ),
                    IconButton(
                      tooltip: 'Discard',
                      icon: const Icon(Icons.delete_outline),
                      onPressed: _busy
                          ? null
                          : () => _run(
                              widget.database.discardMutation,
                              mutationId,
                            ),
                    ),
                  ],
                ),
                isThreeLine: true,
              );
            },
          );
        },
      ),
    );
  }
}
