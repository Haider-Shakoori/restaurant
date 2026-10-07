enum ConnectionMode {
  local,
  cloud,
  automatic,
}

enum ConnectionChannel {
  local,
  cloud,
}

extension ConnectionModeValue on ConnectionMode {
  String get storageValue => name;

  static ConnectionMode parse(String? value) {
    return ConnectionMode.values.firstWhere(
      (mode) => mode.name == value,
      orElse: () => ConnectionMode.automatic,
    );
  }
}

extension ConnectionChannelValue on ConnectionChannel {
  String get storageValue => name;

  static ConnectionChannel parse(String? value) {
    return ConnectionChannel.values.firstWhere(
      (channel) => channel.name == value,
      orElse: () => ConnectionChannel.cloud,
    );
  }
}
