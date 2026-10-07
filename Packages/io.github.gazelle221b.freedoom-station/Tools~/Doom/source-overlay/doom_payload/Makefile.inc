# Doom sources and generated WAD data live below ignored doom_payload/cache/build.
DOOM_JOBS ?= 16
DOOM_POSIX_PATH := /usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
DOOM_NEWLIB_PREFIX ?= /var/tmp/rvc-doom-newlib/bin/riscv32-unknown-elf-
DOOM_IWAD ?= freedoom
DOOM_RAM_BYTES ?= 0x07b00000
DOOM_REPRO_ENV := SOURCE_DATE_EPOCH=1791072000 TZ=UTC LC_ALL=C
DOOM_BR_VERSION := BR2_VERSION_FULL=2022.02.1-rvc-repro-v1
.PHONY: doom-toolchain doom-emdoom doom-rootfs doom-linux-payload doom-run doom-probe
doom-toolchain:
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/prepare_rootfs.py
	PATH=$(DOOM_POSIX_PATH) $(DOOM_REPRO_ENV) $(MAKE) -C $(BUILDROOT) $(DOOM_BR_VERSION) -j$(DOOM_JOBS) toolchain
doom-emdoom: doom-toolchain
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/build_emdoom.py --iwad $(DOOM_IWAD)
doom-rootfs: doom-emdoom
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/prepare_rootfs.py
	PATH=$(DOOM_POSIX_PATH) $(DOOM_REPRO_ENV) $(MAKE) -C $(BUILDROOT) $(DOOM_BR_VERSION) -j$(DOOM_JOBS)
doom-linux-payload: doom-rootfs
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/build_linux.py --jobs $(DOOM_JOBS)
	python3 doom_payload/make_dts.py --memory $(DOOM_RAM_BYTES)
doom-run: doom-linux-payload $(TARGET)
	./rvc -b linux_payload.bin -d doom_payload/build/doom.dtb
.PHONY: doom-auto-payload
doom-auto-payload: doom-linux-payload
	python3 doom_payload/make_dts.py --auto --memory $(DOOM_RAM_BYTES)
	python3 doom_payload/make_dts.py --auto --verify --memory $(DOOM_RAM_BYTES)
	python3 doom_payload/make_dts.py --auto --verify --ascii-trace --memory $(DOOM_RAM_BYTES)
doom-probe:
	mkdir -p doom_payload/build
	$(CC) $(CFLAGS) doom_payload/rvc_probe.c tinypngout/TinyPngOut.c -o doom_payload/build/rvc-probe -L./elfy/target/release -Wl,--no-as-needed -ldl -lpthread -lelfy -lm

.PHONY: doom-newlib-toolchain doom-baremetal doom-baremetal-run
doom-newlib-toolchain:
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/build_newlib_toolchain.py --jobs $(DOOM_JOBS)
doom-baremetal:
	PATH=$(DOOM_POSIX_PATH) python3 doom_payload/build_baremetal.py --prefix $(DOOM_NEWLIB_PREFIX) --jobs $(DOOM_JOBS)
doom-baremetal-run: doom-baremetal doom-probe
	RVC_DOOM_FRAMEBUFFER=doom_payload/build/baremetal/frame ./doom_payload/build/rvc-probe -x -e doom_payload/build/baremetal/doom.elf
