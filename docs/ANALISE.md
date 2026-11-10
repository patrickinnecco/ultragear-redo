# Análise de compatibilidade — LG Switch 7.80 e LG Calibration Studio 7.5 no LG 27GX704A

Monitor analisado: **LG 27GX704A** (OLED, EDID `GSM5CEA`, nome "LG ULTRAGEAR+", firmware 3.07, MCCS 2.1), conectado por DisplayPort a uma RTX 5070. Data: 09/11/2026.

## Método
1. **Telas e fluxos:** cada tela do LG Switch foi aberta e lida pela árvore de acessibilidade do Windows (UI Automation), com capturas de tela. Ficaram registrados os controles exibidos, os ocultos e os desativados para este modelo.
2. **Comportamento real:** foram observadas as chamadas DDC/CI públicas (`dxva2.dll`) que o serviço do LG Switch faz ao mudar cada controle. Cada código e valor foi então conferido lendo o monitor.
3. **Modos que só existem no OSD:** cada código foi enviado ao monitor e o nome foi confirmado no menu do próprio monitor (RTS, Vívida, Leitor, Efeito HDR, sRGB e SMART ENERGY SAVING).
4. **Manual do 27GX704A** (PDF), usado como referência das funções do OSD.

Nenhum código, biblioteca, imagem, vídeo ou ícone da LG foi copiado. O Ultragear usa apenas APIs públicas do Windows. Os ícones e as imagens de referência foram desenhados pelo próprio app.

## Mapa de comandos medido (DDC/CI VCP)
| Função | VCP | Valores | Verificação |
|---|---|---|---|
| Modo de imagem | 0x15 | Personalizada 65, Jogador 1 45, Jogador 2 46, FPS 30, RTS 31, Vívida 49, Leitor 1, Efeito HDR 39, sRGB 15, DCI-P3 25, Fraqueza de cor 6, Calibração 1 18, Calibração 2 19 | LG Switch + OSD + autoteste |
| Brilho / Contraste / Nitidez | 0x10 / 0x12 / 0x87 | 0–100 | autoteste |
| Estabilizador de preto | 0xF9 | lê 0–100; grava em passos de 5 (valor ÷ 5) | LG Switch + autoteste |
| Temperatura de cor | 0x14 | Quente 3, Médio 5, Frio 9, Personalizada 11, Manual 0xFFDE | LG Switch + autoteste |
| Temperatura manual | 0x69 | Kelvin (4000–10000) | LG Switch + autoteste |
| Ganhos R/G/B | 0x16/0x18/0x1A | 0–100 (em "Personalizada") | autoteste |
| Adaptive-Sync/VRR | 0xF8 | 0 desligado, 1 ligado (o monitor ressincroniza ~3 s) | LG Switch |
| SMART ENERGY SAVING | 0xF6 | 0 desligado, 1 baixo, 2 elevado | OSD + autoteste |
| Volume / Mudo | 0x62 / 0x8D | 0–100 / 1 mudo, 2 com som | autoteste |
| Firmware / uso / MCCS | 0xC9 / 0xC0 / 0xDF | somente leitura | leitura |

Observações:
- O monitor devolve a *string de capacidades* com checksum inválido. Por isso o app usa o mapa medido acima em vez de descobrir as funções sozinho.
- Os ajustes ficam guardados **por modo de imagem** no próprio monitor. Ao trocar de modo, o app relê tudo.

## LG Switch — funções e situação no Ultragear
| Função do LG Switch | Situação no 27GX704A | No Ultragear |
|---|---|---|
| Ajustes da tela: modos de imagem | O LG Switch mostra 8 modos | **13 modos** (os 8 + os 5 do OSD), verificados |
| Brilho, contraste, estabilizador de preto | Disponível | Implementado e verificado |
| Nitidez | Oculta no LG Switch, mas o monitor aceita | Implementado e verificado |
| Tempo de resposta | Oculto; o monitor responde 255 (sem suporte) | Não incluído (sem suporte no hardware) |
| Adaptive-Sync/VRR | Disponível | Implementado, com espera da ressincronização |
| Temperatura de cor + Manual + R/G/B | Disponível | Implementado e verificado |
| Orientação da tela | Disponível (função do Windows) | Implementado, com reversão automática em 15 s |
| My Display Preset (modo por aplicativo) | Disponível | Implementado ("Predefinição por aplicativo") e verificado |
| Assistente de Imagem Personalizada | Usa um protocolo proprietário da LG (VCP 0xDB) e imagens da LG | **Recriado** com fluxo próprio: 5 passos que ajustam ao vivo brilho, contraste, estabilizador, temperatura e nitidez do modo Personalizada. O protocolo e as imagens da LG não foram reproduzidos. |
| AI Scene Optimization / AI Picture Mode / AI Upscaling / AI Sound | Desativados pelo LG Switch para este modelo | Não aplicável |
| USB Switch / Dual-Mode / Aspect Ratio | Ocultos para este modelo | Não aplicável |
| Divisão de tela (Smart Screen Split) | Disponível (função do Windows) | Implementado: 10 layouts + personalizado, encaixe ao arrastar, atalhos Alt+Shift+L e Alt+Shift+Setas, verificado |
| webOS Monitor Settings | Só para monitores webOS | Não aplicável |
| Video Call | — | **Excluído a seu pedido** |
| Life Mode (vídeos e papéis de parede, agenda Google) | Conteúdo baixado dos servidores da LG | **Excluído a seu pedido** (também é conteúdo protegido) |
| Atualização do app, atualização de firmware, registro do produto, site e manual da LG | Serviços da LG | **Excluído a seu pedido**; a atualização de firmware também depende de protocolo e arquivos proprietários |
| Iniciar com o Windows | Disponível | Implementado (registro do usuário, sem admin) |

## LG Calibration Studio — funções e situação
| Função | Exige | No Ultragear |
|---|---|---|
| Calibração por hardware (brilho, ponto de branco, gama, gamut gravados em Calibração 1/2) | Colorímetro suportado + SDKs dos fabricantes de sensor + protocolo proprietário de gravação de LUT no monitor | **Não reproduzível.** Alternativa: calibre com o LG Calibration Studio e use o Ultragear para ativar Calibração 1/2. Para calibração por software, gere um perfil ICC (ex.: DisplayCAL/ArgyllCMS) e associe-o pelo Ultragear. |
| Validação (ΔE), DICOM, 3D LUT, HDR (PQ clip/knee), agendamento | Colorímetro + protocolo proprietário | Não reproduzível (mesmo motivo) |
| Detecção do sensor | — | Implementado (USB de X-Rite/Calibrite, Datacolor, Konica Minolta, pontes usadas por Klein/Gamma Scientific) |
| Seleção do modo calibrado | — | Implementado (Calibração 1/2, verificado) |
| Perfis ICC | — | Implementado: listar, importar e definir o perfil padrão **só deste monitor** (API WCS do Windows), verificado com reversão |
| Visualização / conferência | — | Padrões de teste próprios em tela cheia, pixel a pixel (rampa de cinza, níveis de preto e branco, gama 2.2, barras de cor, cores sólidas), com fechamento automático em 3 min (cuidado OLED) |

## Funções do OSD que não podem ser controladas pelo PC neste modelo
Testadas ou procuradas via DDC/CI, sem código aceito. Continuam disponíveis pelo joystick do monitor:
- **Gama de cor** (Modo 1–4): o código 0xFE é só leitura; o monitor ignora a escrita.
- **Seis cores** (matiz/saturação), **Nível de preto** (HDMI), **Luminosidade máxima**, **Crosshair**, **Contador de FPS**, **Movimento/Proteção/Limpeza de imagem OLED**, **Modo de hibernização**, **Suspensão automática**, **OSD Bloquear**, **Sinal sonoro**, **Versão de compatibilidade de entrada**: não aparecem entre os códigos que o monitor responde.
- **Seleção de entrada (0x60)** e **desligar (0xD6)**: o monitor responde, mas não foram incluídas. Se o PC trocar a entrada ou desligar a tela, o canal DDC/CI se perde e não há como voltar pelo software.
- **Redefinir imagem / padrões de fábrica**: não incluídas, para não apagar sem querer os ajustes de todos os modos. Use o OSD. No app, "Meus perfis" serve de cópia de segurança.

## Resultados dos testes no monitor real
- `relatorios-de-teste/autoteste.txt`: **45 OK, 0 falhas.** Cada escrita foi confirmada por leitura e o estado final ficou igual ao inicial.
- `relatorios-de-teste/autoteste-windows.txt`: **9 OK, 0 falhas.** 32/32 zonas exatas, predefinição por app, perfil ICC com reversão.
- Comparação com o LG Switch: depois de o Ultragear restaurar o modo Jogador 2 (brilho 100, contraste 70, estabilizador 60), o LG Switch exibiu exatamente esses valores.
- VRR: o comando foi validado observando o LG Switch, mas não foi exercido pelo autoteste do Ultragear, porque você optou por não incluí-lo. Rode `Ultragear.exe --selftest --vrr` para conferir.
