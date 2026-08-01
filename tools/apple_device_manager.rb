#!/usr/bin/env ruby
# frozen_string_literal: true

# apple_device_manager.rb — gerencia devices da conta Apple Developer (Personal
# Team gratuito) via spaceship (gem do fastlane).
#
# Problema que resolve: o Personal Team free tem teto de devices registrados.
# Ao estourar, o build falha com "Your development team has reached the
# maximum number of registered iPhone devices." A API da Apple não tem delete
# de device — só PATCH de status (ENABLED/DISABLED) — mas DISABLED libera a
# quota do mesmo jeito. Esse é o mesmo endpoint (developerservices2.apple.com,
# protocolo QH65B2) já validado como funcional em conta free pelo patch do
# xtool deste repo (docs/gerenciar-devices-apple-xtool.md, 2026-07-18) — aqui
# reimplementado com a gem `spaceship` em vez de compilar um xtool custom.
#
# Ver tools/README-apple-device-manager.md para contexto completo.
#
# Uso:
#   ruby apple_device_manager.rb list   [--team <TEAM_ID>]
#   ruby apple_device_manager.rb disable <UDID-ou-nome> [--team <TEAM_ID>] [--keep-udid <UDID>]
#   ruby apple_device_manager.rb enable  <UDID-ou-nome> [--team <TEAM_ID>]
#
# TEAM_ID: --team, senão env MABEL_APPLE_TEAM_ID, senão spaceship pede pra
# escolher (se o Apple ID tiver mais de um team).
#
# Segurança (inegociável): login sempre interativo nesta execução — Apple ID,
# senha (via IO.console.getpass, nunca aparece no terminal) e 2FA (prompt
# nativo do spaceship) são pedidos a cada execução. Nada é gravado em disco:
# desligamos o cache de senha no Keychain (FASTLANE_DONT_STORE_PASSWORD) e
# sobrescrevemos Client#store_cookie como no-op — por padrão o spaceship
# grava um cookie de sessão em ~/.fastlane/spaceship/<user>/cookie pra evitar
# repetir o 2FA depois; aqui isso é desligado de propósito, mesmo pagando o
# custo de digitar o 2FA de novo a cada uso.

require "io/console"

ENV["FASTLANE_DONT_STORE_PASSWORD"] = "1"
ENV["FASTLANE_HIDE_CHANGELOG"] = "1"
ENV["FASTLANE_SKIP_UPDATE_CHECK"] = "1"

require "spaceship"

module Spaceship
  class Client
    # Sobrescrito de propósito: nunca persistir cookie de sessão em disco.
    # A sessão fica só na cookie jar em memória (@cookie), válida pelo
    # tempo deste processo.
    def store_cookie(path: nil)
      nil
    end
  end
end

def usage
  puts <<~USAGE
    Uso:
      ruby apple_device_manager.rb list   [--team <TEAM_ID>]
      ruby apple_device_manager.rb disable <UDID-ou-nome> [--team <TEAM_ID>] [--keep-udid <UDID>]
      ruby apple_device_manager.rb enable  <UDID-ou-nome> [--team <TEAM_ID>]

    TEAM_ID: flag --team, senão a env MABEL_APPLE_TEAM_ID, senão o spaceship
    pede pra escolher (se o Apple ID tiver mais de um team associado).

    --keep-udid <UDID>  (só em disable) recusa a ação se o device escolhido
    for esse UDID — proteção extra pra nunca desabilitar o device que você
    está tentando usar agora.
  USAGE
end

def parse_args(argv)
  args = argv.dup
  opts = { team: ENV["MABEL_APPLE_TEAM_ID"], keep_udid: nil }

  if (i = args.index("--team"))
    args.delete_at(i)
    opts[:team] = args.delete_at(i)
  end

  if (i = args.index("--keep-udid"))
    args.delete_at(i)
    opts[:keep_udid] = args.delete_at(i)
  end

  [args, opts]
end

def prompt_login
  print "Apple ID: "
  user = STDIN.gets&.strip
  password = IO.console.getpass("Senha (nao aparece na tela, nao e salva): ")
  abort("Apple ID e senha sao obrigatorios.") if user.to_s.empty? || password.to_s.empty?
  [user, password]
end

def login!(team_id)
  user, password = prompt_login
  puts "Autenticando... (se a conta tiver 2FA, o codigo de 6 digitos sera pedido a seguir)"
  Spaceship::Portal.login(user, password)
  Spaceship::Portal.client.team_id = team_id if team_id
  puts "Login OK. Team ativo: #{Spaceship::Portal.client.team_id}"
rescue Spaceship::Client::InvalidUserCredentialsError => e
  abort("Falha de autenticacao: #{e.message}")
end

def find_device(term)
  Spaceship::Device.find_by_udid(term, include_disabled: true) ||
    Spaceship::Device.find_by_name(term, include_disabled: true)
end

def cmd_list
  devices = Spaceship::Device.all(include_disabled: true)
  if devices.empty?
    puts "Nenhum device registrado."
    return
  end

  puts format("%-4s %-28s %-12s %-45s %s", "St", "Nome", "Modelo", "UDID", "Tipo")
  devices.each do |d|
    status = d.enabled? ? "ON" : "OFF"
    puts format("%-4s %-28s %-12s %-45s %s", status, d.name, (d.model || "-"), d.udid, d.device_type)
  end
end

def cmd_set_status(term, enable:, keep_udid: nil)
  device = find_device(term)
  abort("Device nao encontrado (por UDID ou nome): #{term}") unless device

  if keep_udid && device.udid.to_s.casecmp(keep_udid) == 0
    abort("Recusado: --keep-udid protege o UDID #{keep_udid} de ser desabilitado.")
  end

  action = enable ? "HABILITAR" : "DESABILITAR"
  puts "Device selecionado:"
  puts "  Nome:  #{device.name}"
  puts "  UDID:  #{device.udid}"
  puts "  Tipo:  #{device.device_type} (#{device.model || 'modelo desconhecido'})"
  puts "  Status atual: #{device.enabled? ? 'ENABLED' : 'DISABLED'}"
  print "Confirma #{action} este device? (y/N) "
  answer = STDIN.gets&.strip&.downcase
  unless answer == "y"
    puts "Cancelado."
    return
  end

  enable ? device.enable! : device.disable!

  puts "OK: #{device.name} agora esta #{device.enabled? ? 'ENABLED' : 'DISABLED'}."
  puts "Apple nao tem delete de device — DISABLED libera o slot de provisioning do mesmo jeito." unless enable
end

command = ARGV.shift

if command.nil? || !%w[list enable disable].include?(command)
  usage
  exit(command.nil? ? 0 : 1)
end

rest, opts = parse_args(ARGV)

login!(opts[:team])

case command
when "list"
  cmd_list
when "disable"
  term = rest.shift
  abort("Uso: disable <UDID-ou-nome>") unless term
  cmd_set_status(term, enable: false, keep_udid: opts[:keep_udid])
when "enable"
  term = rest.shift
  abort("Uso: enable <UDID-ou-nome>") unless term
  cmd_set_status(term, enable: true)
end
