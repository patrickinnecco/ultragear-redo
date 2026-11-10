# Usando os mesmos ajustes no Linux (ddcutil)

O Ultragear é um app Windows, mas o monitor obedece aos mesmos comandos DDC/CI em qualquer sistema. No Linux, o [`ddcutil`](https://www.ddcutil.com/) envia esses comandos.

> **Status:** o mapa de códigos abaixo foi medido no 27GX704A (no Windows). Os comandos `ddcutil` em si **ainda não foram testados no Linux**. Confira com `getvcp` depois de cada `setvcp`.

## Preparação
```bash
sudo apt install ddcutil          # Fedora: sudo dnf install ddcutil · Arch: sudo pacman -S ddcutil
sudo modprobe i2c-dev
echo i2c-dev | sudo tee /etc/modules-load.d/i2c-dev.conf
sudo usermod -aG i2c "$USER"      # saia e entre de novo na sessão
ddcutil detect                    # deve listar "LG ULTRAGEAR+" (mfg GSM)
```
Para usar o monitor certo quando houver mais de uma tela, use `--mfg GSM` ou o barramento mostrado no `detect` (`--bus N`). Nos exemplos abaixo, `D="--mfg GSM"`.

**Placa NVIDIA (driver proprietário):** se o `detect` não encontrar o monitor pela DisplayPort, a documentação do ddcutil recomenda criar `/etc/modprobe.d/nvidia-i2c.conf` com:
```
options nvidia NVreg_RegistryDwords=RMUseSwI2c=0x01;RMI2cSpeed=100
```
Depois reinicie o computador.

O 27GX704A devolve a string de capacidades com checksum inválido (`ddcutil capabilities` pode falhar). Isso não impede `getvcp` e `setvcp`.

## Mapa de comandos
| Ajuste | Comando |
|---|---|
| Modo de imagem | `ddcutil $D setvcp 0x15 <código>` |
| Brilho / Contraste / Nitidez (0–100) | `setvcp 0x10 N` / `setvcp 0x12 N` / `setvcp 0x87 N` |
| Estabilizador de preto (0–100, passos de 5) | `setvcp 0xF9 <valor÷5>` (ex.: 50 → `setvcp 0xF9 10`); a leitura devolve 0–100 |
| Temperatura de cor | `setvcp 0x14 3` Quente · `5` Médio · `9` Frio · `11` Personalizada (R/G/B) · `0xFFDE` Manual |
| Kelvin (com Manual ativo) | `setvcp 0x69 6500` (4000–10000) |
| Ganhos R/G/B (com Personalizada) | `setvcp 0x16 N` / `0x18 N` / `0x1A N` |
| Adaptive-Sync/VRR | `setvcp 0xF8 1` liga · `0` desliga (a tela pisca ~3 s) |
| SMART ENERGY SAVING | `setvcp 0xF6 0` desligado · `1` baixo · `2` elevado |
| Volume / Mudo | `setvcp 0x62 N` / `setvcp 0x8D 1` mudo · `2` com som |
| Leitura | `ddcutil $D getvcp 15 10 12 14 F8` |

### Códigos de modo de imagem (0x15)
| Modo | Código | | Modo | Código |
|---|---|---|---|---|
| Imagem Personalizada | 65 | | Leitor | 1 |
| Jogador 1 | 45 | | Efeito HDR | 39 |
| Jogador 2 | 46 | | sRGB | 15 |
| FPS | 30 | | DCI-P3 | 25 |
| RTS | 31 | | Fraqueza de cor | 6 |
| Vívida | 49 | | Calibração 1 / 2 | 18 / 19 |

### Atalhos de exemplo (`~/.bashrc`)
```bash
D="--mfg GSM"
alias lg-jogo1='ddcutil $D setvcp 0x15 45'
alias lg-jogo2='ddcutil $D setvcp 0x15 46'
alias lg-fps='ddcutil $D setvcp 0x15 30'
alias lg-srgb='ddcutil $D setvcp 0x15 15'
lg-brilho() { ddcutil $D setvcp 0x10 "$1"; }      # lg-brilho 60
lg-status() { ddcutil $D getvcp 15 10 12 14 F8 F6; }
```

## O que não tem equivalente direto no Linux
- **Divisão de tela, modo por aplicativo, bandeja:** dependem do gerenciador de janelas.
  - GNOME: extensões de *tiling* (ex.: Tiling Assistant).
  - KDE Plasma: KWin com *Quick Tile*.
  - Modo por aplicativo: um script que observe a janela ativa e chame `ddcutil`.
- **Perfil ICC:** GNOME (Configurações › Cor) ou KDE (colord-kde). Para calibrar com sensor, use DisplayCAL/ArgyllCMS.
- **Rotação:** pelas configurações de tela do ambiente, ou `xrandr --output <saída> --rotate left` no X11.
